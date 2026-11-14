using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Alife.Foundation;
using Alife.Function.FunctionCaller;
using Alife.Framework;
using Microsoft.Extensions.Logging;

namespace Suran.GroupFileManager;

public partial class GroupFileManagerModule
{
    // AI 函数
    // ============================================================

    [XmlFunction(FunctionMode.OneShot)]
    [Description("获取QQ群根目录的文件和文件夹列表")]
    public async Task ListGroupFiles(
        [Description("QQ群号")] long groupId)
    {
        if (CheckFeaturePermission(groupId, "list_files") == false)
        {
            return;
        }
        (List<JsonElement> Files, List<JsonElement> Folders) root = await GetRootEntriesAsync(groupId);
        interactor.Poke(FormatFileList(root.Files, root.Folders, Configuration.MaxFilesList));
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("查看QQ群指定文件夹内的文件列表，folderId与folderName二选一")]
    public async Task ListFolderFiles(
        [Description("QQ群号")] long groupId,
        [Description("文件夹ID")] string folderId = null,
        [Description("文件夹名称（与folderId二选一）")] string folderName = null)
    {
        if (CheckFeaturePermission(groupId, "list_files") == false)
        {
            return;
        }
        string resolvedFolderId = await ResolveFolderIdAsync(groupId, folderId, folderName);
        if (resolvedFolderId.Length == 0)
        {
            interactor.Poke("❌ 请提供文件夹ID或文件夹名称（未找到同名文件夹）");
            return;
        }
        List<JsonElement> files = await GetFolderFilesAsync(groupId, resolvedFolderId);
        if (files.Count == 0)
        {
            interactor.Poke("📂 文件夹内没有文件");
            return;
        }
        StringBuilder builder = new();
        builder.Append("📂 文件夹内的文件（").Append(files.Count).Append("个）：\n");
        int index = 0;
        foreach (JsonElement file in files.Take(Configuration.MaxFilesList))
        {
            index++;
            builder.Append('\n').Append(index).Append(". ").Append(GetEntryName(file, "未知文件"))
                .Append("\n   💾 ").Append(FormatFileSize(GetNumericField(file, "file_size")))
                .Append(" | 👤 ").AppendLine(GetStringField(file, "uploader_name").Length > 0 ? GetStringField(file, "uploader_name") : "未知用户");
        }
        interactor.Poke(builder.ToString());
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("按文件名关键词搜索QQ群文件（不区分大小写模糊匹配）。默认搜索根目录及所有文件夹，folderId或folderName可限定范围（folderName填'根目录'仅搜根目录）。结果带file_id可直接删除/移动")]
    public async Task SearchGroupFiles(
        [Description("QQ群号")] long groupId,
        [Description("文件名关键词，支持部分匹配")] string keyword,
        [Description("可选，限定文件夹ID，'/'表示仅根目录")] string folderId = null,
        [Description("可选，限定文件夹名称（与folderId二选一）")] string folderName = null)
    {
        if (CheckFeaturePermission(groupId, "list_files") == false)
        {
            return;
        }
        string trimmedKeyword = (keyword ?? "").Trim();
        if (trimmedKeyword.Length == 0)
        {
            interactor.Poke("❌ 搜索关键词不能为空");
            return;
        }
        if (trimmedKeyword.Length > 100)
        {
            interactor.Poke("❌ 搜索关键词过长（最多100个字符）");
            return;
        }
        string targetFolderId = await ResolveOptionalFolderScopeAsync(groupId, folderId, folderName);
        if (targetFolderId == null)
        {
            interactor.Poke("❌ 未找到文件夹: " + folderName);
            return;
        }
        (List<FileEntry> Files, string Scope) collected = await CollectGroupFilesAsync(groupId, targetFolderId);
        List<FileEntry> matched = MatchFilesByKeyword(collected.Files, trimmedKeyword);
        LogDebug("搜索完成: 扫描 " + collected.Files.Count + " 个文件，匹配 " + matched.Count + " 个");
        interactor.Poke(FormatSearchResults(trimmedKeyword, collected.Scope, matched, Configuration.MaxFilesList));
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("在QQ群中创建文件夹")]
    public async Task CreateFolder(
        [Description("QQ群号")] long groupId,
        [Description("要创建的文件夹名称")] string folderName)
    {
        if (CheckFeaturePermission(groupId, "create_folder") == false)
        {
            return;
        }
        if (string.IsNullOrWhiteSpace(folderName))
        {
            interactor.Poke("❌ 文件夹名称不能为空");
            return;
        }
        if (folderName.Length > 50)
        {
            interactor.Poke("❌ 文件夹名称过长");
            return;
        }
        string existingId = await GetFolderIdByNameAsync(groupId, folderName);
        if (existingId.Length > 0)
        {
            interactor.Poke("📁 文件夹 '" + folderName + "' 已存在");
            return;
        }
        // 同时传 folder_name 和 name，兼容 NapCat 与 LLOneBot
        JsonObject parameters = new()
        {
            ["group_id"] = groupId,
            ["folder_name"] = folderName,
            ["name"] = folderName
        };
        string response = await CallActionAsync("create_group_file_folder", parameters);
        JsonElement data = ParseActionResponse(response, "创建文件夹");
        string createdId = GetStringField(data, "folder_id").Length > 0 ? GetStringField(data, "folder_id") : GetStringField(data, "id");
        ClearCache(groupId);
        LogDebug("创建文件夹成功: " + folderName + ", ID: " + createdId);
        interactor.Poke("✅ 成功创建文件夹: " + folderName);
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("删除QQ群中的空文件夹，folderId与folderName二选一")]
    public async Task DeleteFolder(
        [Description("QQ群号")] long groupId,
        [Description("要删除的文件夹名称")] string folderName = null,
        [Description("文件夹ID（与folderName二选一）")] string folderId = null)
    {
        if (CheckFeaturePermission(groupId, "delete_folder") == false)
        {
            return;
        }
        string resolvedFolderId = await ResolveFolderIdAsync(groupId, folderId, folderName);
        if (resolvedFolderId.Length == 0)
        {
            interactor.Poke("❌ 请提供文件夹名称或文件夹ID（未找到同名文件夹）");
            return;
        }
        List<JsonElement> remainingFiles = await GetFolderFilesAsync(groupId, resolvedFolderId);
        if (remainingFiles.Count > 0)
        {
            interactor.Poke("❌ 文件夹不为空，请先删除文件夹内的 " + remainingFiles.Count + " 个文件");
            return;
        }
        JsonObject parameters = new()
        {
            ["group_id"] = groupId,
            ["folder_id"] = resolvedFolderId
        };
        string response = await CallActionAsync("delete_group_folder", parameters);
        ParseActionResponse(response, "删除文件夹");
        ClearCache(groupId);
        LogDebug("删除文件夹成功: " + resolvedFolderId);
        interactor.Poke("✅ 成功删除文件夹: " + (folderName ?? resolvedFolderId));
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("删除QQ群文件（支持批量）。fileIds精确删除（多个用英文逗号分隔）；fileNames按名称删除（多个用英文逗号分隔，名称必须在群内唯一，同名多处会拒绝并列出位置）")]
    public async Task DeleteGroupFiles(
        [Description("QQ群号")] long groupId,
        [Description("要删除的文件ID列表，英文逗号分隔")] string fileIds = null,
        [Description("要删除的文件名列表，英文逗号分隔（名称须唯一）")] string fileNames = null)
    {
        if (CheckFeaturePermission(groupId, "delete_file") == false)
        {
            return;
        }
        List<string> idList = SplitList(fileIds ?? "");
        List<string> nameList = SplitList(fileNames ?? "");
        if (idList.Count == 0 && nameList.Count == 0)
        {
            interactor.Poke("❌ 请提供要删除的文件名或文件ID");
            return;
        }

        // fileIds 精确删除（去重）
        List<string> deleteIds = new();
        List<string> deleteNames = new();
        foreach (string id in idList)
        {
            if (deleteIds.Contains(id) == false)
            {
                deleteIds.Add(id);
            }
        }

        // fileNames 先解析再删：同名多处拒绝执行，防止连坐误删
        List<string> missingNames = new();
        foreach (string name in nameList)
        {
            List<FileEntry> matches = await FindFileMatchesAllAsync(groupId, name);
            if (matches.Count == 0)
            {
                missingNames.Add(name);
                continue;
            }
            if (matches.Count > 1)
            {
                StringBuilder ambiguity = new();
                ambiguity.Append("❌ 群内存在 ").Append(matches.Count).Append(" 个同名文件「").Append(name)
                    .Append("」，为避免误删已取消本次删除，请改用 fileIds 精确指定：");
                foreach (FileEntry match in matches)
                {
                    ambiguity.Append("\n  - 🆔 ").Append(match.FileId).Append("（位于 ").Append(match.Location).Append("）");
                }
                interactor.Poke(ambiguity.ToString());
                return;
            }
            FileEntry resolved = matches[0];
            if (deleteIds.Contains(resolved.FileId))
            {
                continue;
            }
            deleteIds.Add(resolved.FileId);
            deleteNames.Add(name + "（" + resolved.Location + "）");
        }

        if (missingNames.Count > 0)
        {
            interactor.Poke("❌ 未找到文件: " + string.Join("、", missingNames));
            return;
        }
        if (deleteIds.Count == 0)
        {
            interactor.Poke("❌ 未找到要删除的文件");
            return;
        }

        int successCount = 0;
        List<string> failures = new();
        foreach (string fileId in deleteIds)
        {
            try
            {
                JsonObject parameters = new()
                {
                    ["group_id"] = groupId,
                    ["file_id"] = fileId
                };
                string response = await CallActionAsync("delete_group_file", parameters);
                ParseActionResponse(response, "删除文件");
                successCount++;
            }
            catch (Exception deleteError)
            {
                failures.Add(fileId + "：" + ExplainFileIdFailure(deleteError.Message));
            }
        }
        ClearCache(groupId);

        if (failures.Count == 0)
        {
            if (deleteIds.Count == 1)
            {
                string name = deleteNames.Count > 0 ? deleteNames[0] : deleteIds[0];
                interactor.Poke("✅ 成功删除文件: " + name);
            }
            else
            {
                interactor.Poke("✅ 成功删除 " + successCount + " 个文件");
            }
            return;
        }
        // 删除失败必须带原因，否则用户只能看到"删除失败"无从下手
        StringBuilder result = new();
        if (successCount > 0)
        {
            result.Append("⚠️ 成功删除 ").Append(successCount).Append(" 个文件，")
                .Append(failures.Count).Append(" 个失败：\n");
        }
        else
        {
            result.Append("❌ 删除失败：\n");
        }
        result.Append(string.Join("\n", failures.Take(3)));
        interactor.Poke(result.ToString());
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("移动QQ群文件到指定文件夹，folderName填'根目录'或'/'表示移动到根目录。优先用fileId，没给再按文件名定位")]
    public async Task MoveGroupFile(
        [Description("QQ群号")] long groupId,
        [Description("要移动的文件名（使用fileId时可传空字符串）")] string fileName,
        [Description("目标文件夹名称，'根目录'或'/'表示根目录")] string folderName,
        [Description("文件ID（来自列表/搜索结果，优先使用）")] string fileId = null)
    {
        if (CheckFeaturePermission(groupId, "move_file") == false)
        {
            return;
        }
        (FileEntry Resolved, string Error) resolvedFile = await ResolveFileEntryAsync(groupId, fileId, fileName);
        if (resolvedFile.Resolved == null)
        {
            interactor.Poke(resolvedFile.Error);
            return;
        }
        FileEntry fileEntry = resolvedFile.Resolved;
        string displayName = fileEntry.FileName.Length > 0
            ? fileEntry.FileName
            : (string.IsNullOrWhiteSpace(fileName) ? (fileId ?? "") : fileName);
        string targetFolderId = "/";
        if (folderName != "根目录" && folderName != "/" && folderName.Length > 0)
        {
            targetFolderId = await GetFolderIdByNameAsync(groupId, folderName);
            if (targetFolderId.Length == 0)
            {
                interactor.Poke("❌ 未找到文件夹: " + folderName);
                return;
            }
        }
        (bool Success, string Message) moveResult = await MoveFileAsync(
            groupId, fileEntry.FileUuid, fileEntry.LocationFolderId, targetFolderId, displayName);
        if (moveResult.Success)
        {
            ClearCache(groupId);
            string destination = targetFolderId == "/" ? "根目录" : "文件夹 '" + folderName + "'";
            interactor.Poke("✅ 文件 '" + displayName + "' 已移动到" + destination);
            return;
        }
        interactor.Poke("❌ " + moveResult.Message);
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("重命名QQ群文件。优先用fileId；按fileName定位时要求群内唯一。NapCat、LLOneBot、SnowLuma 三端均支持文件重命名")]
    public async Task RenameGroupFile(
        [Description("QQ群号")] long groupId,
        [Description("新文件名（保留扩展名）")] string newName,
        [Description("文件ID（来自列表/搜索结果，优先使用）")] string fileId = null,
        [Description("要重命名的文件名（与fileId二选一，须群内唯一）")] string fileName = null)
    {
        if (CheckFeaturePermission(groupId, "rename_file") == false)
        {
            return;
        }
        string trimmedName = (newName ?? "").Trim();
        if (trimmedName.Length == 0)
        {
            interactor.Poke("❌ 新文件名不能为空");
            return;
        }
        if (trimmedName.Contains('/') || trimmedName.Contains('\\'))
        {
            interactor.Poke("❌ 新文件名不能包含路径分隔符");
            return;
        }
        (FileEntry Resolved, string Error) resolvedFile = await ResolveFileEntryAsync(groupId, fileId, fileName);
        if (resolvedFile.Resolved == null)
        {
            interactor.Poke(resolvedFile.Error);
            return;
        }
        FileEntry resolvedEntry = resolvedFile.Resolved;
        string targetFileId = resolvedEntry.FileId.Length > 0 ? resolvedEntry.FileId : resolvedEntry.FileUuid;
        string parentFolderId = resolvedEntry.LocationFolderId.Length > 0 ? resolvedEntry.LocationFolderId : "/";
        string oldName = resolvedEntry.FileName.Length > 0
            ? resolvedEntry.FileName
            : (string.IsNullOrWhiteSpace(fileName) ? (fileId ?? "") : fileName);

        string cleanDirectory = NormalizeFolderParam(parentFolderId);
        // 三端一致：rename_group_file 收 file_id + current_parent_directory + new_name
        JsonObject parameters = new()
        {
            ["group_id"] = groupId,
            ["file_id"] = targetFileId,
            ["current_parent_directory"] = cleanDirectory,
            ["new_name"] = trimmedName
        };
        try
        {
            string response = await CallActionAsync("rename_group_file", parameters);
            ParseActionResponse(response, "重命名文件");
        }
        catch (Exception renameError)
        {
            interactor.Poke("❌ 重命名失败: " + ExplainFileIdFailure(renameError.Message));
            return;
        }
        ClearCache(groupId);
        interactor.Poke("✅ 已将文件 " + oldName + " 重命名为 " + trimmedName);
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("重命名QQ群文件夹。优先用folderId，也可传folderName定位。注意文件夹重命名只有 LLOneBot/SnowLuma 支持，NapCat 未实现该接口")]
    public async Task RenameGroupFolder(
        [Description("QQ群号")] long groupId,
        [Description("新文件夹名")] string newFolderName,
        [Description("文件夹ID（优先使用）")] string folderId = null,
        [Description("要重命名的文件夹名称（与folderId二选一）")] string folderName = null)
    {
        if (CheckFeaturePermission(groupId, "rename_folder") == false)
        {
            return;
        }
        string trimmedName = (newFolderName ?? "").Trim();
        if (trimmedName.Length == 0)
        {
            interactor.Poke("❌ 新文件夹名不能为空");
            return;
        }
        if (trimmedName.Contains('/') || trimmedName.Contains('\\'))
        {
            interactor.Poke("❌ 新文件夹名不能包含路径分隔符");
            return;
        }
        string targetId;
        string oldName;
        if (string.IsNullOrEmpty(folderId) == false)
        {
            targetId = folderId;
            oldName = await GetFolderNameByIdAsync(groupId, folderId);
            if (oldName.Length == 0)
            {
                oldName = folderId;
            }
        }
        else if (string.IsNullOrEmpty(folderName) == false)
        {
            targetId = await GetFolderIdByNameAsync(groupId, folderName);
            if (targetId.Length == 0)
            {
                interactor.Poke("❌ 未找到文件夹: " + folderName);
                return;
            }
            oldName = folderName;
        }
        else
        {
            interactor.Poke("❌ 请提供 folderId 或 folderName");
            return;
        }

        // LLOneBot 与 SnowLuma 的动作名都是 rename_group_file_folder（NapCat 没有这个动作，会回 1404）
        JsonObject parameters = new()
        {
            ["group_id"] = groupId,
            ["folder_id"] = targetId,
            ["new_folder_name"] = trimmedName
        };
        try
        {
            string response = await CallActionAsync("rename_group_file_folder", parameters);
            ParseActionResponse(response, "重命名文件夹");
        }
        catch (Exception renameError)
        {
            interactor.Poke("❌ 重命名失败: " + renameError.Message);
            return;
        }
        ClearCache(groupId);
        interactor.Poke("✅ 已将文件夹 " + oldName + " 重命名为 " + trimmedName);
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("下载QQ群文件到本地（异步），完成后会主动通知。受扩展名白名单与大小上限约束。优先用fileId，没给再按文件名定位")]
    public async Task DownloadGroupFile(
        [Description("QQ群号")] long groupId,
        [Description("要下载的文件名（使用fileId时可传空字符串）")] string fileName,
        [Description("文件ID（来自列表/搜索结果，优先使用）")] string fileId = null)
    {
        if (CheckFeaturePermission(groupId, "download_file") == false)
        {
            return;
        }
        (FileEntry Resolved, string Error) resolvedFile = await ResolveFileEntryAsync(groupId, fileId, fileName);
        if (resolvedFile.Resolved == null)
        {
            interactor.Poke(resolvedFile.Error);
            return;
        }
        FileEntry fileEntry = resolvedFile.Resolved;
        string actualName = fileEntry.FileName.Length > 0
            ? fileEntry.FileName
            : (string.IsNullOrWhiteSpace(fileName) ? "" : fileName.Trim());
        if (actualName.Length == 0)
        {
            actualName = "群文件_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        }
        string rejection = CheckDownloadAllowed(actualName, fileEntry.Size);
        if (rejection.Length > 0)
        {
            interactor.Poke(rejection);
            return;
        }
        string taskId = groupId + "_" + actualName + "_" + DateTimeOffset.Now.ToUnixTimeSeconds();
        DownloadTaskRecord record = new() { Status = "pending", FileName = actualName, CreatedAt = DateTime.Now };
        pendingDownloadTasks[taskId] = record;
        _ = Task.Run(() => RunDownloadTaskAsync(taskId, groupId, actualName, fileEntry.FileId, fileEntry.Busid), DestroyCancellationToken);
        interactor.Poke("✅ 已开始下载: " + actualName + "\n📋 任务ID: " + taskId + "\n⏳ 下载完成后我会通知你~");
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("检查下载任务状态，taskId来自DownloadGroupFile的返回")]
    public async Task CheckDownloadTask(
        [Description("下载任务ID")] string taskId)
    {
        if (pendingDownloadTasks.TryGetValue(taskId, out DownloadTaskRecord record) == false)
        {
            interactor.Poke("❌ 未找到任务ID: " + taskId);
            return;
        }
        if (record.Status == "pending")
        {
            interactor.Poke("⏳ 下载中，请稍后再问我吧~");
            return;
        }
        if (record.Status == "success")
        {
            pendingDownloadTasks.TryRemove(taskId, out _);
            interactor.Poke("✅ 下载完成！📄 " + record.FileName + "\n💾 " + FormatFileSize(record.FileSize));
            return;
        }
        pendingDownloadTasks.TryRemove(taskId, out _);
        interactor.Poke("❌ 下载失败: " + record.ErrorMessage);
    }

    // ============================================================
    // 下载实现
    // ============================================================

    async Task RunDownloadTaskAsync(string taskId, long groupId, string fileName, string fileId, long busid)
    {
        DownloadTaskRecord record = pendingDownloadTasks[taskId];
        try
        {
            JsonObject urlParameters = new()
            {
                ["group_id"] = groupId,
                ["file_id"] = fileId,
                // busid 是文件条目自带的业务号（同群里 102/104 都出现过），拿不到时按各端默认的 102
                ["busid"] = busid > 0 ? busid : 102
            };
            string urlResponse = await CallActionAsync("get_group_file_url", urlParameters);
            JsonElement urlData = ParseActionResponse(urlResponse, "获取下载链接");
            string downloadUrl = GetStringField(urlData, "url");
            if (downloadUrl.Length == 0)
            {
                record.Status = "failed";
                record.ErrorMessage = "未获取到下载链接";
                return;
            }

            string safeName = SanitizeFileName(fileName);
            string savePath = Path.Combine(downloadDirectory, safeName);
            if (File.Exists(savePath))
            {
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
                string extension = Path.GetExtension(safeName);
                savePath = Path.Combine(downloadDirectory, Path.GetFileNameWithoutExtension(safeName) + "_" + timestamp + extension);
            }

            LogDebug("开始下载文件: " + fileName + " -> " + savePath);
            using CancellationTokenSource downloadTimeout = new(TimeSpan.FromSeconds(Math.Max(1, Configuration.DownloadTimeoutSeconds)));
            using HttpResponseMessage fileResponse = await sharedDownloadClient.GetAsync(downloadUrl, downloadTimeout.Token);
            if (fileResponse.IsSuccessStatusCode == false)
            {
                record.Status = "failed";
                record.ErrorMessage = "HTTP " + (int)fileResponse.StatusCode;
                return;
            }
            // 按实际响应大小二次校验（兜底文件列表中大小缺失的情况）
            long contentLength = fileResponse.Content.Headers.ContentLength ?? 0;
            if (maxFileSizeBytes > 0 && contentLength > maxFileSizeBytes)
            {
                record.Status = "failed";
                record.ErrorMessage = "文件大小 " + FormatFileSize(contentLength) + " 超过限制（" + maxFileSizeBytes / (1024 * 1024) + "MB）";
                logger.LogError("文件超出大小限制，取消下载: {File}", fileName);
                return;
            }
            await using (FileStream fileStream = File.Create(savePath))
            {
                await fileResponse.Content.CopyToAsync(fileStream, downloadTimeout.Token);
            }

            record.Status = "success";
            record.SavePath = savePath;
            record.FileSize = new FileInfo(savePath).Length;
            LogDebug("文件下载完成: " + fileName);
            interactor.Poke("✅ 文件「" + fileName + "」已下载完成，保存在 " + savePath);
        }
        catch (Exception downloadError)
        {
            record.Status = "failed";
            record.ErrorMessage = downloadError.Message;
            logger.LogError("下载异常：{Message}", downloadError.Message);
        }
    }

    // 下载前校验扩展名白名单与大小限制，返回拒绝原因（空串=允许）
    string CheckDownloadAllowed(string fileName, long size)
    {
        if (allowedExtensions.Count > 0)
        {
            string extension = Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant();
            if (allowedExtensions.Contains(extension) == false)
            {
                return "❌ 文件类型「" + (extension.Length > 0 ? extension : "无扩展名") + "」不在允许下载的列表中（允许："
                    + string.Join("、", allowedExtensions) + "）";
            }
        }
        if (maxFileSizeBytes > 0 && size > maxFileSizeBytes)
        {
            return "❌ 文件大小 " + FormatFileSize(size) + " 超过允许下载的最大值（" + maxFileSizeBytes / (1024 * 1024) + "MB）";
        }
        return "";
    }

    static string SanitizeFileName(string fileName)
    {
        char[] invalidCharacters = new[] { '<', '>', ':', '"', '/', '\\', '|', '?', '*' };
        string sanitized = fileName;
        foreach (char invalid in invalidCharacters)
        {
            sanitized = sanitized.Replace(invalid, '_');
        }
        if (sanitized.Length > 200)
        {
            string extension = Path.GetExtension(sanitized);
            sanitized = sanitized[..(200 - extension.Length)] + extension;
        }
        return sanitized;
    }

    // ============================================================
    // 格式化与字段辅助
    // ============================================================

    string FormatFileList(List<JsonElement> files, List<JsonElement> folders, int maxItems)
    {
        List<string> parts = new();
        if (folders.Count > 0)
        {
            parts.Add("📁 文件夹（" + folders.Count + "个）：\n");
            int index = 0;
            foreach (JsonElement folder in folders.Take(maxItems))
            {
                index++;
                parts.Add(index + ". " + GetEntryName(folder, "未知文件夹") + "\n   🆔 ID: " + GetEntryFolderId(folder));
            }
            parts.Add("");
        }
        if (files.Count > 0)
        {
            parts.Add("📄 文件（" + files.Count + "个）：\n");
            int index = 0;
            foreach (JsonElement file in files.Take(maxItems))
            {
                index++;
                string uploader = GetStringField(file, "uploader_name").Length > 0 ? GetStringField(file, "uploader_name") : "未知用户";
                parts.Add(index + ". " + GetEntryName(file, "未知文件")
                    + "\n   💾 " + FormatFileSize(GetNumericField(file, "file_size")) + " | 👤 " + uploader);
            }
        }
        return parts.Count > 0 ? string.Join("\n", parts) : "当前群文件列表为空";
    }

    string FormatSearchResults(string keyword, string scope, List<FileEntry> matchedFiles, int maxItems)
    {
        if (matchedFiles.Count == 0)
        {
            return "🔍 在" + scope + "中未找到文件名包含「" + keyword + "」的文件";
        }
        StringBuilder builder = new();
        builder.Append("🔍 搜索「").Append(keyword).Append("」共找到 ").Append(matchedFiles.Count)
            .Append(" 个文件（").Append(scope).Append("）：\n");
        int index = 0;
        foreach (FileEntry file in matchedFiles.Take(maxItems))
        {
            index++;
            string uploader = GetStringField(file.Source, "uploader_name").Length > 0
                ? GetStringField(file.Source, "uploader_name")
                : "未知用户";
            string dateText = FormatDate(GetNumericField(file.Source, "upload_time"));
            builder.Append('\n').Append(index).Append(". ").Append(file.FileName)
                .Append("\n   📁 ").Append(file.Location)
                .Append(" | 💾 ").Append(FormatFileSize(file.Size))
                .Append(" | 👤 ").Append(uploader);
            if (dateText.Length > 0)
            {
                builder.Append(" | 📅 ").Append(dateText);
            }
            builder.Append("\n   🆔 ").Append(file.FileId);
        }
        if (matchedFiles.Count > maxItems)
        {
            builder.Append("\n\n⚠️ 结果较多，仅显示前 ").Append(maxItems).Append(" 条，请使用更精确的关键词缩小范围");
        }
        return builder.ToString();
    }

    static string FormatDate(long unixSeconds)
    {
        if (unixSeconds <= 0)
        {
            return "";
        }
        long seconds = unixSeconds > 1000000000000 ? unixSeconds / 1000 : unixSeconds;
        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(seconds).LocalDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
        catch
        {
            return "";
        }
    }

    static string FormatFileSize(long sizeBytes)
    {
        if (sizeBytes < 1024)
        {
            return sizeBytes + "B";
        }
        if (sizeBytes < 1024 * 1024)
        {
            return (sizeBytes / 1024d).ToString("F1", CultureInfo.InvariantCulture) + "KB";
        }
        if (sizeBytes < 1024L * 1024 * 1024)
        {
            return (sizeBytes / (1024d * 1024)).ToString("F1", CultureInfo.InvariantCulture) + "MB";
        }
        return (sizeBytes / (1024d * 1024 * 1024)).ToString("F1", CultureInfo.InvariantCulture) + "GB";
    }

    async Task<string> ResolveFolderIdAsync(long groupId, string folderId, string folderName)
    {
        if (string.IsNullOrEmpty(folderId) == false)
        {
            return folderId;
        }
        if (string.IsNullOrEmpty(folderName) == false)
        {
            return await GetFolderIdByNameAsync(groupId, folderName);
        }
        return "";
    }

    // 解析搜索范围：folderId优先；folderName填'根目录'返回"/"；都为空返回空串（全量）；找不到返回null
    async Task<string> ResolveOptionalFolderScopeAsync(long groupId, string folderId, string folderName)
    {
        if (string.IsNullOrEmpty(folderId) == false)
        {
            return folderId;
        }
        if (string.IsNullOrEmpty(folderName) == false)
        {
            if (folderName == "根目录" || folderName == "/")
            {
                return "/";
            }
            string resolved = await GetFolderIdByNameAsync(groupId, folderName);
            if (resolved.Length == 0)
            {
                return null;
            }
            return resolved;
        }
        return "";
    }

    static string GetStringField(JsonElement element, string fieldName)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return "";
        }
        if (element.TryGetProperty(fieldName, out JsonElement field) == false)
        {
            return "";
        }
        return field.ValueKind switch
        {
            JsonValueKind.String => field.GetString() ?? "",
            JsonValueKind.Number => field.GetRawText(),
            _ => ""
        };
    }

    static long GetNumericField(JsonElement element, string fieldName)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return 0;
        }
        if (element.TryGetProperty(fieldName, out JsonElement field) == false)
        {
            return 0;
        }
        return field.ValueKind switch
        {
            JsonValueKind.Number => field.GetInt64(),
            JsonValueKind.String => long.TryParse(field.GetString(), out long parsed) ? parsed : 0,
            _ => 0
        };
    }

    // 下载任务状态记录
    sealed class DownloadTaskRecord
    {
        public string Status { get; set; } = "pending";
        public string FileName { get; set; } = "";
        public string SavePath { get; set; } = "";
        public long FileSize { get; set; }
        public string ErrorMessage { get; set; } = "";
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}