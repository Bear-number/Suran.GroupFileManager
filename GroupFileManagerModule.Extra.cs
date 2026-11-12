// 上传、统计、目录树、下载任务、文本预览、自动归档。
// 连接、协议端探测、群/文件夹解析、权限判定、格式化全部复用 GroupFileManagerModule.cs 的私有辅助，
// 这里只写新动作的参数差异与输出格式。
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Alife.Function.FunctionCaller;
using Microsoft.Extensions.Logging;

namespace Suran.GroupFileManager;

public partial class GroupFileManagerModule
{
    // 递归扫描上限：防止群文件特别多时把协议端和上下文一起拖死
    const int MaxStatFiles = 5000;
    const int MaxStatFolders = 300;
    const int MaxStatDepth = 8;
    const int MaxOrganizeFiles = 100;

    // 可在线预览的文本类扩展名，其余一律提示改用下载
    static readonly HashSet<string> PreviewTextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "txt", "md", "markdown", "csv", "tsv", "json", "jsonl", "log", "xml", "yml", "yaml",
        "ini", "conf", "cfg", "properties", "toml", "html", "htm", "css", "js", "ts",
        "py", "java", "c", "h", "cpp", "hpp", "cs", "go", "rs", "sql", "sh", "bat", "cmd",
        "ps1", "srt", "vtt", "tex"
    };

    // ============================================================
    // 配置解析（每次调用现读，改配置后无需重载插件）
    // ============================================================

    List<string> UploadExtensionList => ParseExtensionList(Configuration.UploadAllowedExtensions);

    long UploadMaxBytes => Math.Max(0, Configuration.MaxUploadSizeMb) * 1024L * 1024L;

    long PreviewMaxBytes => Math.Max(1, Configuration.PreviewMaxKb) * 1024L;

    static List<string> ParseExtensionList(string? raw)
    {
        return (raw ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(extension => extension.Trim().TrimStart('.').ToLowerInvariant())
            .Where(extension => extension.Length > 0)
            .ToList();
    }

    // 归档规则：pdf,docx=文档;jpg,png=图片;zip,7z=压缩包
    static List<(HashSet<string> Extensions, string FolderName)> ParseArchiveRules(string? raw)
    {
        List<(HashSet<string> Extensions, string FolderName)> rules = new();
        foreach (string ruleText in (raw ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int separator = ruleText.IndexOf('=');
            if (separator <= 0 || separator == ruleText.Length - 1)
            {
                continue;
            }
            List<string> extensions = ParseExtensionList(ruleText[..separator]);
            string folderName = ruleText[(separator + 1)..].Trim();
            if (extensions.Count == 0 || folderName.Length == 0)
            {
                continue;
            }
            rules.Add((new HashSet<string>(extensions, StringComparer.OrdinalIgnoreCase), folderName));
        }
        return rules;
    }

    static string TargetFolderOf(string fileName, List<(HashSet<string> Extensions, string FolderName)> rules)
    {
        string extension = Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant();
        foreach ((HashSet<string> Extensions, string FolderName) rule in rules)
        {
            if (rule.Extensions.Contains(extension))
            {
                return rule.FolderName;
            }
        }
        return "";
    }

    // ============================================================
    // fileId 或唯一文件名 → 文件对象（移动/重命名/下载/预览共用）
    // ============================================================

    async Task<(FileEntry? Resolved, string Error)> ResolveFileEntryAsync(long groupId, string? fileId, string? fileName)
    {
        string id = (fileId ?? "").Trim();
        string name = (fileName ?? "").Trim();

        if (id.Length > 0)
        {
            // 扫根目录+一级文件夹定位该 ID 的实际父目录（移动/重命名需要）
            (List<FileEntry> Files, string Scope) collected = await CollectGroupFilesAsync(groupId, null);
            FileEntry? located = collected.Files.FirstOrDefault(entry => entry.FileId == id || entry.FileUuid == id);
            if (located != null)
            {
                return (located, "");
            }
            // 未扫到（可能在更深层目录或列表被截断）：按根目录兜底，行为与旧版一致
            LogDebug("fileId 未在根目录/一级文件夹中定位，按根目录尝试: " + id);
            return (new FileEntry
            {
                FileId = id,
                FileUuid = id,
                FileName = name,
                Location = "未定位",
                LocationFolderId = "/"
            }, "");
        }

        if (name.Length == 0)
        {
            return (null, "❌ 请提供 fileId 或 fileName");
        }

        List<FileEntry> matches = await FindFileMatchesAllAsync(groupId, name);
        if (matches.Count == 0)
        {
            return (null, "❌ 未找到文件: " + name);
        }
        if (matches.Count > 1)
        {
            StringBuilder ambiguity = new();
            ambiguity.Append("❌ 群内存在 ").Append(matches.Count).Append(" 个同名文件「").Append(name)
                .Append("」，请用 fileId 精确指定：");
            foreach (FileEntry match in matches)
            {
                ambiguity.Append("\n  - 🆔 ").Append(match.FileId).Append("（位于 ").Append(match.Location).Append("）");
            }
            return (null, ambiguity.ToString());
        }
        return (matches[0], "");
    }

    // ============================================================
    // 上传
    // ============================================================

    [XmlFunction(FunctionMode.OneShot, "upload_group_file")]
    [Description("上传本地文件到QQ群文件。folderId与folderName留空表示根目录；remoteName留空用本地文件名。受「上传·大小上限」与「上传·扩展名白名单」约束")]
    public async Task UploadGroupFile(
        [Description("QQ群号")] long groupId,
        [Description("本地文件路径（协议端所在机器可访问的路径，最好用绝对路径）")] string localPath,
        [Description("目标文件夹ID，留空=根目录")] string? folderId = null,
        [Description("目标文件夹名称（与folderId二选一，会自动解析成ID）")] string? folderName = null,
        [Description("上传后的文件名，留空用本地文件名")] string? remoteName = null)
    {
        if (CheckFeaturePermission(groupId, "upload_file") == false)
        {
            return;
        }
        (bool Ok, string FullPath, string RemoteName, string Error) source = PrepareUploadSource(localPath, remoteName);
        if (source.Ok == false)
        {
            interactor.Poke(source.Error);
            return;
        }
        (string FolderId, string FolderError) folder = await ResolveUploadFolderAsync(groupId, folderId, folderName);
        if (folder.FolderError.Length > 0)
        {
            interactor.Poke(folder.FolderError);
            return;
        }
        string folderValue = folder.FolderId == "/" ? "" : folder.FolderId;
        (bool Success, string Message) upload = await SendUploadAsync(groupId, source.FullPath, source.RemoteName, folderValue);
        if (upload.Success == false)
        {
            interactor.Poke("❌ 上传失败: " + upload.Message);
            return;
        }
        ClearCache(groupId);
        string folderLabel = folderValue.Length > 0
            ? "文件夹「" + (string.IsNullOrWhiteSpace(folderName) ? folderValue : folderName) + "」"
            : "根目录";
        interactor.Poke("✅ 已上传到群 " + groupId + "：" + source.RemoteName + " → " + folderLabel
            + "\n💾 " + FormatFileSize(new FileInfo(source.FullPath).Length));
    }

    [XmlFunction(FunctionMode.OneShot, "upload_group_files")]
    [Description("批量上传本地文件到QQ群文件，localPaths用英文逗号分隔。逐条报告成功/失败，一条失败不影响其它文件。folderId与folderName作用于全部文件")]
    public async Task UploadGroupFiles(
        [Description("QQ群号")] long groupId,
        [Description("本地文件路径列表，英文逗号分隔")] string localPaths,
        [Description("目标文件夹ID，留空=根目录")] string? folderId = null,
        [Description("目标文件夹名称（与folderId二选一）")] string? folderName = null)
    {
        if (CheckFeaturePermission(groupId, "upload_file") == false)
        {
            return;
        }
        List<string> paths = SplitList(localPaths ?? "");
        if (paths.Count == 0)
        {
            interactor.Poke("❌ 请提供要上传的本地文件路径（英文逗号分隔）");
            return;
        }
        if (paths.Count > 20)
        {
            interactor.Poke("❌ 单次最多上传 20 个文件，请分批执行");
            return;
        }
        (string FolderId, string FolderError) folder = await ResolveUploadFolderAsync(groupId, folderId, folderName);
        if (folder.FolderError.Length > 0)
        {
            interactor.Poke(folder.FolderError);
            return;
        }
        string folderValue = folder.FolderId == "/" ? "" : folder.FolderId;
        string folderLabel = folderValue.Length > 0
            ? "文件夹「" + (string.IsNullOrWhiteSpace(folderName) ? folderValue : folderName) + "」"
            : "根目录";

        StringBuilder result = new();
        result.Append("📤 批量上传（群 ").Append(groupId).Append(" → ").Append(folderLabel)
            .Append("，共 ").Append(paths.Count).Append(" 个）：");
        int successCount = 0;
        int index = 0;
        bool anySuccess = false;
        foreach (string path in paths)
        {
            index++;
            (bool Ok, string FullPath, string RemoteName, string Error) source = PrepareUploadSource(path, null);
            if (source.Ok == false)
            {
                result.Append('\n').Append(index).Append(". ❌ ").Append(path).Append("：").Append(source.Error.TrimStart('❌', ' '));
                continue;
            }
            (bool Success, string Message) upload = await SendUploadAsync(groupId, source.FullPath, source.RemoteName, folderValue);
            if (upload.Success)
            {
                successCount++;
                anySuccess = true;
                result.Append('\n').Append(index).Append(". ✅ ").Append(source.RemoteName);
            }
            else
            {
                result.Append('\n').Append(index).Append(". ❌ ").Append(source.RemoteName).Append("：").Append(upload.Message);
            }
        }
        if (anySuccess)
        {
            ClearCache(groupId);
        }
        result.Append("\n\n✅ 成功 ").Append(successCount).Append(" 个，失败 ")
            .Append(paths.Count - successCount).Append(" 个");
        interactor.Poke(result.ToString());
    }

    // 目标文件夹：folderId 优先，folderName 解析成 ID；留空=根目录
    async Task<(string FolderId, string Error)> ResolveUploadFolderAsync(long groupId, string? folderId, string? folderName)
    {
        string direct = (folderId ?? "").Trim();
        if (direct.Length > 0 && direct != "/")
        {
            return (direct, "");
        }
        string name = (folderName ?? "").Trim();
        if (name.Length == 0 || name == "根目录")
        {
            return ("", "");
        }
        string resolved = await GetFolderIdByNameAsync(groupId, name);
        return resolved.Length > 0 ? (resolved, "") : ("", "❌ 未找到文件夹: " + name);
    }

    // 上传前置校验：存在、大小、扩展名、远程名；返回空错误串表示通过
    (bool Ok, string FullPath, string RemoteName, string Error) PrepareUploadSource(string? localPath, string? remoteName)
    {
        string trimmed = (localPath ?? "").Trim().Trim('"');
        if (trimmed.Length == 0)
        {
            return (false, "", "", "❌ 本地文件路径不能为空");
        }
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(trimmed);
        }
        catch
        {
            return (false, "", "", "❌ 本地文件路径无效: " + localPath);
        }
        if (File.Exists(fullPath) == false)
        {
            return (false, "", "", "❌ 本地文件不存在: " + fullPath);
        }
        string name = (remoteName ?? "").Trim();
        if (name.Length == 0)
        {
            name = Path.GetFileName(fullPath);
        }
        if (name.Length == 0)
        {
            return (false, "", "", "❌ 无法确定上传后的文件名");
        }
        if (name.Contains('/') || name.Contains('\\'))
        {
            return (false, "", "", "❌ 上传后的文件名不能包含路径分隔符");
        }
        long size = new FileInfo(fullPath).Length;
        long maxBytes = UploadMaxBytes;
        if (maxBytes > 0 && size > maxBytes)
        {
            return (false, "", "", "❌ 文件大小 " + FormatFileSize(size) + " 超过上传上限（"
                + (maxBytes / (1024 * 1024)) + "MB）");
        }
        List<string> allowed = UploadExtensionList;
        if (allowed.Count > 0)
        {
            string extension = Path.GetExtension(fullPath).TrimStart('.').ToLowerInvariant();
            if (allowed.Contains(extension) == false)
            {
                return (false, "", "", "❌ 文件类型「" + (extension.Length > 0 ? extension : "无扩展名")
                    + "」不在允许上传的列表中（允许：" + string.Join("、", allowed) + "）");
            }
        }
        return (true, fullPath, name, "");
    }

    async Task<(bool Success, string Message)> SendUploadAsync(long groupId, string fullPath, string remoteName, string folderValue)
    {
        JsonObject parameters = new()
        {
            ["group_id"] = groupId,
            ["file"] = fullPath,
            ["name"] = remoteName,
            ["folder"] = folderValue
        };
        try
        {
            string response = await CallActionAsync("upload_group_file", parameters);
            ParseActionResponse(response, "上传文件");
            return (true, "");
        }
        catch (Exception uploadError)
        {
            return (false, ExplainUploadFailure(uploadError.Message, folderValue.Length > 0));
        }
    }

    // 上传失败原因通常是三件事：协议端不支持 folder 参数、路径在协议端机器上不存在、权限不足
    static string ExplainUploadFailure(string message, bool hasFolder)
    {
        string explained = ExplainFileIdFailure(message);
        if (hasFolder && (explained.Contains("folder", StringComparison.OrdinalIgnoreCase)
            || explained.Contains("文件夹")))
        {
            explained += "（该协议端可能不支持 upload_group_file 的 folder 参数，可把 folderId/folderName 留空上传到根目录）";
        }
        if (explained.Contains("not found", StringComparison.OrdinalIgnoreCase)
            || explained.Contains("no such file", StringComparison.OrdinalIgnoreCase))
        {
            explained += "（请确认该路径在协议端所在机器/容器内可访问）";
        }
        return explained;
    }

    // ============================================================
    // 统计与目录树
    // ============================================================

    sealed class GroupScanState
    {
        public List<(string Name, long Size, string Path)> Files { get; } = new();
        public int FileCount { get; set; }
        public long TotalSize { get; set; }
        public int FolderCount { get; set; }
        public bool Truncated { get; set; }
        public string TruncateReason { get; set; } = "";
        public bool PerFolderLimitHit { get; set; }
    }

    // 与 GetFolderFilesAsync 的区别：同时返回子文件夹，供递归统计与目录树使用（不走5秒缓存）
    async Task<(List<JsonElement> Files, List<JsonElement> Folders)> GetRawFolderEntriesAsync(long groupId, string folderId)
    {
        try
        {
            JsonObject parameters = new()
            {
                ["group_id"] = groupId,
                ["folder_id"] = NormalizeFolderParam(folderId),
                ["file_count"] = SearchFileCount
            };
            string response = await CallActionAsync("get_group_files_by_folder", parameters);
            JsonElement data = ParseActionResponse(response, "获取文件夹内容");
            List<JsonElement> files = new();
            List<JsonElement> folders = new();
            if (data.ValueKind == JsonValueKind.Object)
            {
                if (data.TryGetProperty("files", out JsonElement filesElement) && filesElement.ValueKind == JsonValueKind.Array)
                {
                    files = filesElement.EnumerateArray().Select(item => item.Clone()).ToList();
                }
                if (data.TryGetProperty("folders", out JsonElement foldersElement) && foldersElement.ValueKind == JsonValueKind.Array)
                {
                    folders = foldersElement.EnumerateArray().Select(item => item.Clone()).ToList();
                }
            }
            else if (data.ValueKind == JsonValueKind.Array)
            {
                files = data.EnumerateArray().Select(item => item.Clone()).ToList();
            }
            return (files, folders);
        }
        catch (Exception scanError)
        {
            logger.LogError("遍历文件夹失败（{FolderId}）：{Message}", folderId, scanError.Message);
            return (new List<JsonElement>(), new List<JsonElement>());
        }
    }

    async Task ScanFolderAsync(long groupId, string folderId, string folderPath, int depth, GroupScanState state)
    {
        if (state.Truncated)
        {
            return;
        }
        if (depth > MaxStatDepth)
        {
            state.Truncated = true;
            state.TruncateReason = "超过最大深度 " + MaxStatDepth + " 层";
            return;
        }
        // 根目录用 get_group_root_files（三端都稳），子目录才用 get_group_files_by_folder
        (List<JsonElement> files, List<JsonElement> folders) = folderId == "/"
            ? await GetRootEntriesAsync(groupId, SearchFileCount)
            : await GetRawFolderEntriesAsync(groupId, folderId);
        if (files.Count >= SearchFileCount)
        {
            // 单文件夹一次最多取 200 条，超过就看不全了
            state.PerFolderLimitHit = true;
        }
        foreach (JsonElement file in files)
        {
            if (state.FileCount >= MaxStatFiles)
            {
                state.Truncated = true;
                state.TruncateReason = "文件数达到 " + MaxStatFiles + " 个";
                return;
            }
            state.FileCount++;
            long size = GetNumericField(file, "file_size") > 0
                ? GetNumericField(file, "file_size")
                : GetNumericField(file, "size");
            state.TotalSize += size;
            state.Files.Add((GetEntryName(file, "未知文件"), size, folderPath.Length > 0 ? folderPath : "根目录"));
        }
        foreach (JsonElement folder in folders)
        {
            if (state.FolderCount >= MaxStatFolders)
            {
                state.Truncated = true;
                state.TruncateReason = "文件夹数达到 " + MaxStatFolders + " 个";
                return;
            }
            string childFolderId = GetEntryFolderId(folder);
            if (childFolderId.Length == 0)
            {
                continue;
            }
            state.FolderCount++;
            string childName = GetEntryName(folder, childFolderId);
            string childPath = folderPath.Length > 0 ? folderPath + "/" + childName : childName;
            await ScanFolderAsync(groupId, childFolderId, childPath, depth + 1, state);
            if (state.Truncated)
            {
                return;
            }
        }
    }

    [XmlFunction(FunctionMode.OneShot, "group_file_stats")]
    [Description("递归统计QQ群文件：文件总数、总大小、文件夹数，列出最大的前top个文件与按扩展名汇总（前10类）。扫描有上限，会说明是否截断")]
    public async Task GroupFileStats(
        [Description("QQ群号")] long groupId,
        [Description("最大的文件显示条数，默认10")] int top = 10)
    {
        if (CheckFeaturePermission(groupId, "list_files") == false)
        {
            return;
        }
        int wanted = Math.Clamp(top, 1, 50);
        GroupScanState state = new();
        await ScanFolderAsync(groupId, "/", "", 0, state);

        StringBuilder result = new();
        result.Append("📊 群 ").Append(groupId).Append(" 文件统计：")
            .Append("\n📄 文件：").Append(state.FileCount).Append(" 个 | 💾 总计 ").Append(FormatFileSize(state.TotalSize))
            .Append("\n📁 文件夹：").Append(state.FolderCount).Append(" 个");

        if (state.Files.Count > 0)
        {
            result.Append("\n\n🏆 最大的文件（前 ").Append(wanted).Append(" 个）：");
            int index = 0;
            foreach ((string Name, long Size, string Path) file in state.Files
                .OrderByDescending(item => item.Size)
                .ThenBy(item => item.Name)
                .Take(wanted))
            {
                index++;
                result.Append('\n').Append(index).Append(". ").Append(file.Name)
                    .Append("\n   💾 ").Append(FormatFileSize(file.Size))
                    .Append(" | 📁 ").Append(file.Path);
            }
        }

        List<(string Extension, int Count, long Size)> extensionSummary = state.Files
            .GroupBy(file => Path.GetExtension(file.Name).TrimStart('.').ToLowerInvariant())
            .Select(group => (Extension: group.Key, Count: group.Count(), Size: group.Sum(item => item.Size)))
            .OrderByDescending(item => item.Size)
            .ThenBy(item => item.Extension)
            .Take(10)
            .ToList();
        if (extensionSummary.Count > 0)
        {
            result.Append("\n\n📎 按类型（按大小前 10 类）：");
            foreach ((string extension, int count, long size) in extensionSummary)
            {
                result.Append("\n  - ").Append(extension.Length > 0 ? extension : "无扩展名")
                    .Append("：").Append(count).Append(" 个 | ").Append(FormatFileSize(size));
            }
        }

        if (state.Truncated || state.PerFolderLimitHit)
        {
            result.Append("\n\n⚠️ 统计可能不完整");
            if (state.Truncated)
            {
                result.Append("：").Append(state.TruncateReason);
            }
            if (state.PerFolderLimitHit)
            {
                result.Append("（部分文件夹单次最多读取 ").Append(SearchFileCount).Append(" 条）");
            }
        }
        interactor.Poke(result.ToString());
    }

    sealed class GroupTreeState
    {
        public List<string> Lines { get; } = new();
        public int Count { get; set; }
        public bool Truncated { get; set; }
        public string TruncateReason { get; set; } = "";
    }

    async Task BuildTreeAsync(
        long groupId, string folderId, int depth, int maxDepth, int maxEntries, GroupTreeState state)
    {
        if (state.Truncated)
        {
            return;
        }
        (List<JsonElement> files, List<JsonElement> folders) = folderId == "/"
            ? await GetRootEntriesAsync(groupId, SearchFileCount)
            : await GetRawFolderEntriesAsync(groupId, folderId);
        string indent = new string(' ', depth * 2);
        foreach (JsonElement file in files)
        {
            if (state.Count >= maxEntries)
            {
                state.Truncated = true;
                state.TruncateReason = "条目数达到 " + maxEntries + " 个";
                return;
            }
            state.Count++;
            state.Lines.Add(indent + "📄 " + GetEntryName(file, "未知文件") + " (" + FormatFileSize(
                GetNumericField(file, "file_size") > 0 ? GetNumericField(file, "file_size") : GetNumericField(file, "size")) + ")");
        }
        foreach (JsonElement folder in folders)
        {
            if (state.Count >= maxEntries)
            {
                state.Truncated = true;
                state.TruncateReason = "条目数达到 " + maxEntries + " 个";
                return;
            }
            string childFolderId = GetEntryFolderId(folder);
            if (childFolderId.Length == 0)
            {
                continue;
            }
            state.Count++;
            state.Lines.Add(indent + "📁 " + GetEntryName(folder, childFolderId) + "/");
            if (depth >= maxDepth)
            {
                state.Truncated = true;
                state.TruncateReason = "达到最大深度 " + maxDepth + " 层";
                continue;
            }
            await BuildTreeAsync(groupId, childFolderId, depth + 1, maxDepth, maxEntries, state);
            if (state.Truncated)
            {
                return;
            }
        }
    }

    [XmlFunction(FunctionMode.OneShot, "group_file_tree")]
    [Description("输出QQ群文件目录树（缩进表示层级），maxDepth控制最大层数、maxEntries控制最多条目数，超出会截断并提示")]
    public async Task GroupFileTree(
        [Description("QQ群号")] long groupId,
        [Description("最大层级，默认5")] int maxDepth = 5,
        [Description("最多显示的条目数，默认200")] int maxEntries = 200)
    {
        if (CheckFeaturePermission(groupId, "list_files") == false)
        {
            return;
        }
        int depthLimit = Math.Clamp(maxDepth, 1, 10);
        int entryLimit = Math.Clamp(maxEntries, 1, 500);
        GroupTreeState state = new();
        await BuildTreeAsync(groupId, "/", 0, depthLimit, entryLimit, state);
        if (state.Lines.Count == 0)
        {
            interactor.Poke("🌳 群 " + groupId + " 根目录为空");
            return;
        }
        StringBuilder result = new();
        result.Append("🌳 群 ").Append(groupId).Append(" 目录树（最多 ").Append(depthLimit)
            .Append(" 层 / ").Append(entryLimit).Append(" 项）：\n");
        result.Append(string.Join("\n", state.Lines));
        if (state.Truncated)
        {
            result.Append("\n\n⚠️ 已截断：").Append(state.TruncateReason).Append("，可调大 maxDepth/maxEntries 后重试");
        }
        interactor.Poke(result.ToString());
    }

    // ============================================================
    // 下载任务列表
    // ============================================================

    [XmlFunction(FunctionMode.OneShot, "list_download_tasks")]
    [Description("列出本插件当前的下载任务与最近完成的下载（任务ID、文件名、状态、大小、本地路径、时间），按时间倒序")]
    public async Task ListDownloadTasks(
        [Description("显示条数，默认10")] int limit = 10)
    {
        int wanted = Math.Clamp(limit, 1, 50);
        List<KeyValuePair<string, DownloadTaskRecord>> tasks = pendingDownloadTasks
            .OrderByDescending(pair => pair.Value.CreatedAt)
            .Take(wanted)
            .ToList();
        if (tasks.Count == 0)
        {
            interactor.Poke("📋 当前没有下载任务");
            return;
        }
        StringBuilder result = new();
        result.Append("📋 下载任务（共 ").Append(pendingDownloadTasks.Count).Append(" 个，按时间倒序，显示前 ")
            .Append(tasks.Count).Append(" 个）：");
        int index = 0;
        foreach (KeyValuePair<string, DownloadTaskRecord> pair in tasks)
        {
            index++;
            DownloadTaskRecord record = pair.Value;
            string statusText = record.Status switch
            {
                "pending" => "⏳ 下载中",
                "success" => "✅ 已完成",
                "failed" => "❌ 失败",
                _ => record.Status
            };
            result.Append('\n').Append(index).Append(". ").Append(record.FileName.Length > 0 ? record.FileName : "未知文件")
                .Append("\n   ").Append(statusText)
                .Append(" | 💾 ").Append(record.FileSize > 0 ? FormatFileSize(record.FileSize) : "未知")
                .Append(" | 🕒 ").Append(record.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            if (record.SavePath.Length > 0)
            {
                result.Append("\n   📁 ").Append(record.SavePath);
            }
            if (record.ErrorMessage.Length > 0)
            {
                result.Append("\n   ⚠️ ").Append(record.ErrorMessage);
            }
            result.Append("\n   🆔 ").Append(pair.Key);
        }
        await Task.CompletedTask;
        interactor.Poke(result.ToString());
    }

    // ============================================================
    // 文本预览
    // ============================================================

    [XmlFunction(FunctionMode.OneShot, "preview_group_file")]
    [Description("在线预览QQ群里的文本文件（txt/md/csv/json/log/xml/yml等）：下载到临时目录后只读前若干行/字符，用完即删。二进制文件请改用 DownloadGroupFile")]
    public async Task PreviewGroupFile(
        [Description("QQ群号")] long groupId,
        [Description("文件名（与fileId二选一）")] string? fileName = null,
        [Description("文件ID（优先使用）")] string? fileId = null,
        [Description("最多预览行数，默认60")] int lines = 60,
        [Description("最多预览字符数，默认4000")] int maxChars = 4000)
    {
        if (CheckFeaturePermission(groupId, "download_file") == false)
        {
            return;
        }
        (FileEntry? Resolved, string Error) resolvedFile = await ResolveFileEntryAsync(groupId, fileId, fileName);
        if (resolvedFile.Resolved == null)
        {
            interactor.Poke(resolvedFile.Error);
            return;
        }
        FileEntry fileEntry = resolvedFile.Resolved;
        string displayName = fileEntry.FileName.Length > 0 ? fileEntry.FileName : (fileName ?? "").Trim();
        string extension = Path.GetExtension(displayName).TrimStart('.').ToLowerInvariant();
        if (displayName.Length == 0 || PreviewTextExtensions.Contains(extension) == false)
        {
            interactor.Poke("❌ 「" + (displayName.Length > 0 ? displayName : fileId ?? "该文件")
                + "」不是可预览的文本类型，请改用 DownloadGroupFile 下载后查看");
            return;
        }
        int lineLimit = Math.Clamp(lines, 1, 500);
        int charLimit = Math.Clamp(maxChars, 100, 20000);
        long byteLimit = PreviewMaxBytes;

        string? tempPath = null;
        try
        {
            JsonObject urlParameters = new()
            {
                ["group_id"] = groupId,
                ["file_id"] = fileEntry.FileId.Length > 0 ? fileEntry.FileId : fileEntry.FileUuid,
                ["busid"] = fileEntry.Busid > 0 ? fileEntry.Busid : 102
            };
            string urlResponse = await CallActionAsync("get_group_file_url", urlParameters);
            JsonElement urlData = ParseActionResponse(urlResponse, "获取预览链接");
            string downloadUrl = GetStringField(urlData, "url");
            if (downloadUrl.Length == 0)
            {
                interactor.Poke("❌ 未获取到下载链接，无法预览");
                return;
            }

            string tempDirectory = Path.Combine(Path.GetTempPath(), "SuranGroupFilePreview");
            Directory.CreateDirectory(tempDirectory);
            tempPath = Path.Combine(tempDirectory, Guid.NewGuid().ToString("N")
                + (extension.Length > 0 ? "." + extension : ".txt"));

            using CancellationTokenSource previewTimeout = new(TimeSpan.FromSeconds(Math.Max(1, Configuration.DownloadTimeoutSeconds)));
            using HttpResponseMessage previewResponse = await sharedDownloadClient.GetAsync(
                downloadUrl, HttpCompletionOption.ResponseHeadersRead, previewTimeout.Token);
            if (previewResponse.IsSuccessStatusCode == false)
            {
                interactor.Poke("❌ 预览下载失败：HTTP " + (int)previewResponse.StatusCode);
                return;
            }
            long declaredLength = previewResponse.Content.Headers.ContentLength ?? 0;
            bool truncatedByBytes = declaredLength > byteLimit;
            long written = 0;
            await using (Stream contentStream = await previewResponse.Content.ReadAsStreamAsync(previewTimeout.Token))
            await using (FileStream tempStream = File.Create(tempPath))
            {
                byte[] buffer = new byte[64 * 1024];
                while (written < byteLimit)
                {
                    int want = (int)Math.Min(buffer.Length, byteLimit - written);
                    int read = await contentStream.ReadAsync(buffer.AsMemory(0, want), previewTimeout.Token);
                    if (read <= 0)
                    {
                        break;
                    }
                    await tempStream.WriteAsync(buffer.AsMemory(0, read), previewTimeout.Token);
                    written += read;
                }
                if (written >= byteLimit)
                {
                    truncatedByBytes = true;
                }
            }

            StringBuilder preview = new();
            bool truncatedByLines = false;
            bool truncatedByChars = false;
            using (StreamReader reader = new(tempPath, Encoding.UTF8, true))
            {
                int lineNumber = 0;
                while (lineNumber < lineLimit)
                {
                    string? line = await reader.ReadLineAsync();
                    if (line == null)
                    {
                        break;
                    }
                    lineNumber++;
                    if (preview.Length + line.Length + 1 > charLimit)
                    {
                        int remain = charLimit - preview.Length;
                        if (remain > 0)
                        {
                            preview.Append(line[..Math.Min(remain, line.Length)]);
                        }
                        truncatedByChars = true;
                        break;
                    }
                    preview.AppendLine(line);
                }
                if (truncatedByChars == false && reader.Peek() >= 0)
                {
                    truncatedByLines = true;
                }
            }

            StringBuilder result = new();
            result.Append("👀 「").Append(displayName).Append("」预览（前 ").Append(lineLimit)
                .Append(" 行 / 最多 ").Append(charLimit).Append(" 字）：\n────\n")
                .Append(preview.ToString().TrimEnd()).Append("\n────");
            if (truncatedByBytes || truncatedByLines || truncatedByChars)
            {
                result.Append("\n⚠️ 内容已截断");
                List<string> reasons = new();
                if (truncatedByBytes)
                {
                    reasons.Add("只读取前 " + FormatFileSize(byteLimit));
                }
                if (truncatedByLines)
                {
                    reasons.Add("仅显示前 " + lineLimit + " 行");
                }
                if (truncatedByChars)
                {
                    reasons.Add("仅显示前 " + charLimit + " 字");
                }
                result.Append("（").Append(string.Join("，", reasons)).Append("）");
            }
            interactor.Poke(result.ToString());
        }
        catch (Exception previewError)
        {
            interactor.Poke("❌ 预览失败: " + ExplainFileIdFailure(previewError.Message));
        }
        finally
        {
            if (tempPath != null)
            {
                try
                {
                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
                }
                catch (Exception cleanupError)
                {
                    LogDebug("预览临时文件删除失败: " + cleanupError.Message);
                }
            }
        }
    }

    // ============================================================
    // 自动归档
    // ============================================================

    [XmlFunction(FunctionMode.OneShot, "auto_organize_group_files")]
    [Description("按「归档·规则」把QQ群根目录的文件移动到对应文件夹（不存在则创建）。dryRun为true时只输出计划不执行；单次最多处理100个文件，受移动权限约束")]
    public async Task AutoOrganizeGroupFiles(
        [Description("QQ群号")] long groupId,
        [Description("是否只预览计划（true=不执行，false=真正移动），默认true")] bool dryRun = true)
    {
        if (CheckFeaturePermission(groupId, "move_file") == false)
        {
            return;
        }
        List<(HashSet<string> Extensions, string FolderName)> rules = ParseArchiveRules(Configuration.ArchiveRules);
        if (rules.Count == 0)
        {
            interactor.Poke("❌ 未配置「归档·规则」，请先在插件设置里填写，例如 pdf,docx=文档;jpg,png=图片;zip,7z=压缩包");
            return;
        }

        (List<JsonElement> Files, List<JsonElement> Folders) root = await GetRootEntriesAsync(groupId, SearchFileCount);
        List<(FileEntry Entry, string TargetFolder)> planned = new();
        bool overLimit = false;
        foreach (JsonElement file in root.Files)
        {
            string name = GetEntryName(file);
            if (name.Length == 0)
            {
                continue;
            }
            string targetFolder = TargetFolderOf(name, rules);
            if (targetFolder.Length == 0)
            {
                continue;
            }
            if (planned.Count >= MaxOrganizeFiles)
            {
                overLimit = true;
                break;
            }
            planned.Add((ToFileEntry(file, "根目录", "/"), targetFolder));
        }

        if (planned.Count == 0)
        {
            interactor.Poke("🔍 群 " + groupId + " 根目录没有匹配归档规则的文件");
            return;
        }

        StringBuilder result = new();
        if (dryRun)
        {
            result.Append("🗂️ 归档计划（群 ").Append(groupId).Append("，试运行，未做任何改动）：");
            foreach (IGrouping<string, (FileEntry Entry, string TargetFolder)> group in planned.GroupBy(item => item.TargetFolder))
            {
                result.Append("\n📦 ").Append(group.Key).Append("（").Append(group.Count()).Append(" 个）：");
                foreach ((FileEntry entry, string _) in group)
                {
                    result.Append("\n  - ").Append(entry.FileName).Append("（").Append(FormatFileSize(entry.Size)).Append("）");
                }
            }
            if (overLimit)
            {
                result.Append("\n\n⚠️ 匹配文件超过单次上限 ").Append(MaxOrganizeFiles).Append(" 个，仅处理前 ")
                    .Append(MaxOrganizeFiles).Append(" 个，其余请再次运行");
            }
            result.Append("\n\n💡 确认无误后把 dryRun 设为 false 执行");
            interactor.Poke(result.ToString());
            return;
        }

        Dictionary<string, string> folderIds = new();
        List<string> failures = new();
        int successCount = 0;
        int createdFolders = 0;
        foreach (IGrouping<string, (FileEntry Entry, string TargetFolder)> group in planned.GroupBy(item => item.TargetFolder))
        {
            string targetFolderId;
            try
            {
                if (folderIds.TryGetValue(group.Key, out string? cachedId) == false)
                {
                    (string FolderId, bool Created) ensured = await EnsureFolderAsync(groupId, group.Key);
                    cachedId = ensured.FolderId;
                    folderIds[group.Key] = cachedId;
                    if (ensured.Created)
                    {
                        createdFolders++;
                    }
                }
                targetFolderId = cachedId;
            }
            catch (Exception createError)
            {
                foreach ((FileEntry entry, string _) in group)
                {
                    failures.Add(entry.FileName + "：" + createError.Message);
                }
                continue;
            }
            if (targetFolderId.Length == 0)
            {
                foreach ((FileEntry entry, string _) in group)
                {
                    failures.Add(entry.FileName + "：无法创建或定位文件夹「" + group.Key + "」");
                }
                continue;
            }
            foreach ((FileEntry entry, string _) in group)
            {
                (bool Moved, string Message) move = await MoveFileAsync(
                    groupId, entry.FileUuid, entry.LocationFolderId, targetFolderId, entry.FileName);
                if (move.Moved)
                {
                    successCount++;
                }
                else
                {
                    failures.Add(entry.FileName + "：" + move.Message);
                }
            }
        }
        ClearCache(groupId);

        result.Append("🗂️ 归档完成（群 ").Append(groupId).Append("）：成功 ").Append(successCount)
            .Append(" 个，失败 ").Append(failures.Count).Append(" 个");
        if (createdFolders > 0)
        {
            result.Append("\n📁 新建文件夹：").Append(createdFolders).Append(" 个");
        }
        foreach ((FileEntry entry, string targetFolder) in planned.Take(20))
        {
            result.Append("\n  - ").Append(entry.FileName).Append(" → ").Append(targetFolder);
        }
        if (planned.Count > 20)
        {
            result.Append("\n  … 其余 ").Append(planned.Count - 20).Append(" 个已省略");
        }
        if (failures.Count > 0)
        {
            result.Append("\n\n❌ 失败明细：");
            foreach (string failure in failures.Take(5))
            {
                result.Append("\n  - ").Append(failure);
            }
            if (failures.Count > 5)
            {
                result.Append("\n  … 其余 ").Append(failures.Count - 5).Append(" 条失败已省略");
            }
        }
        if (overLimit)
        {
            result.Append("\n\n⚠️ 匹配文件超过单次上限 ").Append(MaxOrganizeFiles).Append(" 个，本次仅处理前 ")
                .Append(MaxOrganizeFiles).Append(" 个");
        }
        interactor.Poke(result.ToString());
    }

    // 归档目标文件夹：已存在就直接用，不存在则创建并回查 ID
    async Task<(string FolderId, bool Created)> EnsureFolderAsync(long groupId, string folderName)
    {
        string existingId = await GetFolderIdByNameAsync(groupId, folderName);
        if (existingId.Length > 0)
        {
            return (existingId, false);
        }
        JsonObject parameters = new()
        {
            ["group_id"] = groupId,
            ["folder_name"] = folderName,
            ["name"] = folderName
        };
        string response = await CallActionAsync("create_group_file_folder", parameters);
        JsonElement data = ParseActionResponse(response, "创建归档文件夹");
        string createdId = GetStringField(data, "folder_id").Length > 0
            ? GetStringField(data, "folder_id")
            : GetStringField(data, "id");
        if (createdId.Length == 0)
        {
            ClearCache(groupId);
            createdId = await GetFolderIdByNameAsync(groupId, folderName);
        }
        return (createdId, true);
    }
}
