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

public class GroupFileManagerConfig
{
    [DisplayName("运行·功能文档常驻")]
    [Description("开：功能说明常驻系统提示词，模型随时看得到工具；关：隐式加载省 token，模型先输出入口标签再读文档。群文件操作多是主人明确要求时用，默认关")]
    public bool DocumentAlwaysResident { get; set; } = false;

    // ---- 基础设置 ----
    [DisplayName("基础·协议端地址")]
    [Description("留空 = 自动跟随「QQ 聊天」插件的配置（推荐）。填了就用这里的，例如 ws://127.0.0.1:3001")]
    public string OneBotWsUrl { get; set; } = "";

    [DisplayName("基础·访问令牌")]
    [Description("与协议端配置的 token 一致。留空 = 跟随「QQ 聊天」插件。填了地址却没填这里，协议端一开 token 就会连不上")]
    public string OneBotToken { get; set; } = "";

    [DisplayName("基础·列表最大条数")]
    [Description("列出/搜索文件时最多显示的条目数量")]
    public int MaxFilesList { get; set; } = 20;

    [DisplayName("基础·调试模式")]
    [Description("开启后日志输出API调用、搜索、缓存等详细过程")]
    public bool DebugMode { get; set; } = false;

    [DisplayName("基础·协议端类型")]
    [Description("协议端类型，auto 自动探测，也可指定 napcat / llonebot / snowluma。")]
    public string ApiProvider { get; set; } = "auto";

    // ---- 下载设置 ----
    [DisplayName("下载·保存目录")]
    [Description("相对存储目录的相对路径（禁止..和盘符），默认 files，即 {存储目录}/files")]
    public string DownloadPath { get; set; } = "files";

    [DisplayName("下载·大小上限(MB)")]
    [Description("允许下载的最大文件大小，0为不限制")]
    public int MaxFileSizeMb { get; set; } = 100;

    [DisplayName("下载·扩展名白名单")]
    [Description("允许下载的扩展名，英文逗号分隔（如 pdf,jpg,zip），留空允许所有类型")]
    public string AllowedExtensions { get; set; } = "";

    [DisplayName("下载·超时(秒)")]
    [Description("下载超时时间")]
    public int DownloadTimeoutSeconds { get; set; } = 60;

    // ---- 上传设置 ----
    [DisplayName("上传·大小上限(MB)")]
    [Description("允许上传到群文件的本地文件最大体积，0为不限制，默认200")]
    public int MaxUploadSizeMb { get; set; } = 200;

    [DisplayName("上传·扩展名白名单")]
    [Description("允许上传的扩展名，英文逗号分隔（如 pdf,jpg,zip），留空允许所有类型")]
    public string UploadAllowedExtensions { get; set; } = "";

    // ---- 预览与归档 ----
    [DisplayName("预览·最大 KB")]
    [Description("在线预览群文件时最多读取的大小（KB），默认512。超出部分不读取，仅支持文本类文件")]
    public int PreviewMaxKb { get; set; } = 512;

    [DisplayName("归档·规则")]
    [Description("自动归档规则，格式 扩展名=文件夹名，多条用英文分号分隔，例如 pdf,docx=文档;jpg,png=图片;zip,7z=压缩包。留空则自动归档功能不可用")]
    public string ArchiveRules { get; set; } = "";

    // ---- 权限设置 ----
    [DisplayName("权限·权限模式")]
    [Description("deny_list=黑名单（默认，所有群可用按需禁用）/ allow_list=白名单（仅允许名单内的群）")]
    public string PermissionMode { get; set; } = "deny_list";

    [DisplayName("权限·白名单群号")]
    [Description("白名单模式下允许使用群文件功能的群号，英文逗号分隔；留空等于全部禁用")]
    public string EnabledGroups { get; set; } = "";

    [DisplayName("权限·禁用创建文件夹的群")]
    [Description("群号英文逗号分隔，填 all 全局禁用，留空不限制")]
    public string DisabledCreateFolder { get; set; } = "";

    [DisplayName("权限·禁用删除文件夹的群")]
    [Description("群号英文逗号分隔，填 all 全局禁用，留空不限制")]
    public string DisabledDeleteFolder { get; set; } = "";

    [DisplayName("权限·禁用删除文件的群")]
    [Description("群号英文逗号分隔，填 all 全局禁用，留空不限制")]
    public string DisabledDeleteFile { get; set; } = "";

    [DisplayName("权限·禁用移动文件的群")]
    [Description("群号英文逗号分隔，填 all 全局禁用，留空不限制")]
    public string DisabledMoveFile { get; set; } = "";

    [DisplayName("权限·禁用重命名文件的群")]
    [Description("群号英文逗号分隔，填 all 全局禁用，留空不限制")]
    public string DisabledRenameFile { get; set; } = "";

    [DisplayName("权限·禁用重命名文件夹的群")]
    [Description("群号英文逗号分隔，填 all 全局禁用，留空不限制")]
    public string DisabledRenameFolder { get; set; } = "";

    [DisplayName("权限·禁用下载文件的群")]
    [Description("群号英文逗号分隔，填 all 全局禁用，留空不限制")]
    public string DisabledDownloadFile { get; set; } = "";

    [DisplayName("权限·禁用查看列表的群")]
    [Description("群号英文逗号分隔，填 all 全局禁用；同时控制搜索功能")]
    public string DisabledListFiles { get; set; } = "";

    [DisplayName("权限·禁用上传的群")]
    [Description("群号英文逗号分隔，填 all 全局禁用，留空不限制")]
    public string DisabledUploadFile { get; set; } = "";
}

[Module("群文件管家",
    "QQ群文件管理：列表/搜索/重命名/文件夹/删除/移动/异步下载，黑白名单双权限模式，自动适配NapCat、LLOneBot与SnowLuma",
    defaultCategory: "苏染的工具/QQ与群管理",
    EditorUI = typeof(GroupFileManagerUI))]
public partial class GroupFileManagerModule(
    XmlFunctionCaller functionCaller,
    ILogger<GroupFileManagerModule> logger,
    Interactor<GroupFileManagerModule> interactor
) : ChatBehaviour, IConfigurable<GroupFileManagerConfig>
{
    public GroupFileManagerConfig Configuration { get; set; } = null!;

    /// <summary>
    /// 协议端连接信息。
    ///
    /// 地址留空时跟随「QQ 聊天」插件的配置——那是唯一值得人填一次的地方。
    /// 换端口、给协议端开 token，都不用回来改这个插件，读它的配置就够。
    /// 读不到就退回上一个版本的默认值，行为和以前完全一致。
    /// </summary>
    (string Url, string Token, string Source) ResolveOneBot()
    {
        string url = (Configuration.OneBotWsUrl ?? "").Trim();
        string token = (Configuration.OneBotToken ?? "").Trim();

        if (url.Length > 0)
            return (url, token, "本插件");

        // 按角色存，所以换角色读的是那个角色自己的 QQ 配置——不同角色可能是不同 bot 账号，这是对的。
        // 第二条是兜底：StorageFolderPath 若已含角色层、或 StorageKey 为空，靠第一条拼不出来。
        string[] candidates =
        [
            Path.Combine(AlifePath.StorageFolderPath, Character.StorageKey,
                "Configuration", "Alife.Function.QChat.QChatService.json"),
            Path.Combine(AlifePath.StorageFolderPath,
                "Configuration", "Alife.Function.QChat.QChatService.json"),
        ];

        foreach (string path in candidates)
        {
            try
            {
                if (!File.Exists(path))
                    continue;

                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
                string sharedUrl = document.RootElement.TryGetProperty("Url", out JsonElement u) ? u.GetString() ?? "" : "";
                string sharedToken = document.RootElement.TryGetProperty("Token", out JsonElement t) ? t.GetString() ?? "" : "";
                if (sharedUrl.Trim().Length > 0)
                    return (sharedUrl.Trim(), token.Length > 0 ? token : sharedToken.Trim(), "QQ 聊天插件");
            }
            catch (Exception)
            {
                // 这个候选读不了就试下一个。配置坏了也不该让发消息失败在读取配置上
            }
        }

        return ("ws://127.0.0.1:3001", token, "默认");
    }

    /// <summary>
    /// 建连并鉴权。
    /// token 必须在 ConnectAsync 之前写进请求头——连上之后 ClientWebSocket 不允许再改 Options。
    /// </summary>
    async Task ConnectOneBotAsync(ClientWebSocket connection, CancellationToken cancellationToken)
    {
        (string url, string token, _) = ResolveOneBot();
        if (token.Length > 0)
            connection.Options.SetRequestHeader("Authorization", "Bearer " + token);
        await connection.ConnectAsync(new Uri(url), cancellationToken);
    }


    // 单次向协议端拉取的文件数量（get_group_*files 的 file_count 参数）
    const int SearchFileCount = 200;

    // ---- 连接层 ----
    static readonly SemaphoreSlim sendLock = new(1, 1);
    static readonly ConcurrentDictionary<string, TaskCompletionSource<string>> pendingResponses = new();
    static ClientWebSocket sharedConnection;
    static TaskCompletionSource<bool> connectionReady = CreateReadySource();
    static readonly HttpClient sharedDownloadClient = new() { Timeout = TimeSpan.FromMinutes(10) };

    static bool receiveLoopStarted;

    // ---- 文件夹内容缓存（5秒有效）与下载任务 ----
    readonly Dictionary<string, (DateTime CachedTime, List<JsonElement> Files)> folderCache = new();
    readonly ConcurrentDictionary<string, DownloadTaskRecord> pendingDownloadTasks = new();
    string downloadDirectory = "";
    List<string> allowedExtensions = new();
    long maxFileSizeBytes;
    // 协议端类型："auto" / "napcat" / "llonebot" / "snowluma"，来自配置
    string apiProvider = "auto";
    // 移动接口实测生效的参数格式：""=未试出来，napcat / onebot（LLOneBot 与 SnowLuma 同格式）
    string moveParameterStyle = "";
    // get_version_info 识别出的协议端署名，只用于日志与错误提示
    static string providerName = "";

    // ============================================================
    // 生命周期
    // ============================================================

    protected override Task OnAwake()
    {
        XmlHandler handler = new(this)
        {
            Description = "QQ群文件管理工具集：查看/搜索/重命名/创建删除文件夹/删除移动文件/异步下载群文件。",
            Explanation = "删除与移动等写操作需要Bot是群管理员。搜索结果带 file_id，可直接用于删除/移动/重命名。按文件名操作时同名文件多处存在会被拒绝并列出位置，防止误删。"
        };
        functionCaller.RegisterHandler(handler,
            (Configuration?.DocumentAlwaysResident ?? false) ? DocumentMode.Explicit : DocumentMode.Implicit,
            cancellationToken: DestroyCancellationToken);
        return Task.CompletedTask;
    }

    protected override Task OnStart()
    {
        apiProvider = Configuration.ApiProvider.Trim().ToLowerInvariant();
        if (apiProvider != "napcat" && apiProvider != "llonebot" && apiProvider != "snowluma")
        {
            apiProvider = "auto";
        }

        // 下载目录：仅允许相对路径，挂在存储目录下防止越权
        string downloadPath = (Configuration.DownloadPath ?? "files").Trim().Trim('/', '\\');
        if (downloadPath.Length == 0 || downloadPath.Contains("..") || downloadPath.Contains(':'))
        {
            logger.LogWarning("DownloadPath 配置非法（仅支持相对路径），已回退为 files");
            downloadPath = "files";
        }
        downloadDirectory = Path.Combine(AlifePath.StorageFolderPath, downloadPath);
        Directory.CreateDirectory(downloadDirectory);

        // 扩展名白名单：去空格去点转小写，空列表允许全部
        allowedExtensions = Configuration.AllowedExtensions
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(extension => extension.Trim().TrimStart('.').ToLowerInvariant())
            .ToList();

        long maxFileSizeMb = Math.Max(0, Configuration.MaxFileSizeMb);
        maxFileSizeBytes = maxFileSizeMb * 1024 * 1024;

        if (Configuration.DebugMode)
        {
            logger.LogInformation("[文件管家] 🔧 调试模式已开启，将显示详细日志");
        }
        LogDebug("下载目录: " + downloadDirectory);
        if (Configuration.PermissionMode == "allow_list")
        {
            logger.LogInformation("[文件管家] 白名单模式，允许的群: {Groups}", Configuration.EnabledGroups);
        }
        StartReceiveLoop();
        StartProviderCheck();
        return Task.CompletedTask;
    }

    protected override async Task OnDestroy()
    {
        await sendLock.WaitAsync();
        try
        {
            sharedConnection?.Dispose();
            sharedConnection = null;
            pendingResponses.Clear();
            receiveLoopStarted = false;
            providerName = "";
        }
        finally
        {
            sendLock.Release();
        }
        logger.LogInformation("[文件管家] 👋 已卸载");
    }

    // ============================================================
    // OneBot 连接层（常驻接收循环统一完成 echo 响应配对）
    // ============================================================

    static TaskCompletionSource<bool> CreateReadySource()
    {
        TaskCompletionSource<bool> source = new(TaskCreationOptions.RunContinuationsAsynchronously);
        return source;
    }

    void StartReceiveLoop()
    {
        if (receiveLoopStarted)
        {
            return;
        }
        receiveLoopStarted = true;
        Task.Run(async () =>
        {
            while (DestroyCancellationToken.IsCancellationRequested == false)
            {
                try
                {
                    sharedConnection?.Dispose();
                    ClientWebSocket connection = new();
                    using CancellationTokenSource connectTimeout = new(TimeSpan.FromSeconds(10));
                    await ConnectOneBotAsync(connection, connectTimeout.Token);
                    sharedConnection = connection;
                    connectionReady.TrySetResult(true);
                    LogDebug("协议端已连接：" + ResolveOneBot().Url);
                    await ReceiveLoopCoreAsync(connection);
                }
                catch (Exception loopError)
                {
                    logger.LogWarning("协议端连接中断，3秒后重连：{Message}", loopError.Message);
                }
                connectionReady = CreateReadySource();
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(3), DestroyCancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }, DestroyCancellationToken);
    }

    async Task ReceiveLoopCoreAsync(ClientWebSocket connection)
    {
        byte[] receiveBuffer = new byte[1024 * 1024];
        while (DestroyCancellationToken.IsCancellationRequested == false)
        {
            MemoryStream messageStream = new();
            WebSocketReceiveResult receiveResult;
            do
            {
                receiveResult = await connection.ReceiveAsync(
                    new ArraySegment<byte>(receiveBuffer), DestroyCancellationToken);
                if (receiveResult.MessageType == WebSocketMessageType.Close)
                {
                    throw new Exception("协议端主动断开连接");
                }
                messageStream.Write(receiveBuffer, 0, receiveResult.Count);
            }
            while (receiveResult.EndOfMessage == false);

            string messageText = Encoding.UTF8.GetString(messageStream.ToArray());
            CompletePendingResponse(messageText);
        }
    }

    static void CompletePendingResponse(string messageText)
    {
        JsonElement messageElement;
        try
        {
            using JsonDocument parsedMessage = JsonDocument.Parse(messageText);
            messageElement = parsedMessage.RootElement.Clone();
        }
        catch
        {
            return;
        }
        if (messageElement.TryGetProperty("echo", out JsonElement echoElement) && echoElement.ValueKind == JsonValueKind.String)
        {
            string echo = echoElement.GetString() ?? "";
            if (pendingResponses.TryRemove(echo, out TaskCompletionSource<string> waiter))
            {
                waiter.TrySetResult(messageText);
            }
        }
        // 其余为事件推送，本插件不消费
    }

    async Task<string> CallActionAsync(string action, JsonObject parameters)
    {
        await connectionReady.Task.WaitAsync(TimeSpan.FromSeconds(15));
        string echo = Guid.NewGuid().ToString("N");
        TaskCompletionSource<string> waiter = new(TaskCreationOptions.RunContinuationsAsynchronously);
        pendingResponses[echo] = waiter;

        await sendLock.WaitAsync();
        try
        {
            if (sharedConnection == null || sharedConnection.State != WebSocketState.Open)
            {
                connectionReady = CreateReadySource();
                sharedConnection?.Dispose();
                ClientWebSocket connection = new();
                using CancellationTokenSource connectTimeout = new(TimeSpan.FromSeconds(10));
                await ConnectOneBotAsync(connection, connectTimeout.Token);
                sharedConnection = connection;
                connectionReady.TrySetResult(true);
            }

            JsonObject payload = new()
            {
                ["action"] = action,
                ["params"] = parameters,
                ["echo"] = echo
            };
            byte[] payloadBytes = Encoding.UTF8.GetBytes(payload.ToJsonString());
            using CancellationTokenSource sendTimeout = new(TimeSpan.FromSeconds(30));
            await sharedConnection.SendAsync(
                new ArraySegment<byte>(payloadBytes), WebSocketMessageType.Text, true, sendTimeout.Token);
        }
        catch
        {
            pendingResponses.TryRemove(echo, out _);
            throw;
        }
        finally
        {
            sendLock.Release();
        }

        string responseText = await waiter.Task.WaitAsync(TimeSpan.FromSeconds(30));
        return responseText;
    }

    // 解析动作响应：失败抛出带协议端信息的异常
    static JsonElement ParseActionResponse(string responseText, string operationName)
    {
        using JsonDocument parsedResponse = JsonDocument.Parse(responseText);
        JsonElement root = parsedResponse.RootElement.Clone();
        string status = root.TryGetProperty("status", out JsonElement statusElement) && statusElement.ValueKind == JsonValueKind.String
            ? statusElement.GetString() ?? ""
            : "";
        int retcode = root.TryGetProperty("retcode", out JsonElement retcodeElement) && retcodeElement.ValueKind == JsonValueKind.Number
            ? retcodeElement.GetInt32()
            : -1;
        bool succeeded = status == "ok" || status == "async" || retcode == 0;
        if (succeeded == false)
        {
            // SnowLuma 的失败包只填 wording（LLOneBot/NapCat 两个键都填同一个值），只读 message 在 SnowLuma 下永远是「未知错误」
            string detail = GetStringField(root, "message");
            if (detail.Length == 0)
            {
                detail = GetStringField(root, "wording");
            }
            if (detail.Length == 0)
            {
                detail = "未知错误";
            }
            // 1404 = 动作名在该协议端不存在（三端都有的接口不多，文件夹重命名只有 LLOneBot/SnowLuma 有）
            if (retcode == 1404)
            {
                detail += "（" + (providerName.Length > 0 ? providerName : "该协议端")
                    + " 未实现该接口，请检查协议端设置或更换支持的协议端）";
            }
            throw new Exception(operationName + "失败：" + detail);
        }
        return root.TryGetProperty("data", out JsonElement dataElement) ? dataElement.Clone() : JsonDocument.Parse("null").RootElement.Clone();
    }

    // ============================================================
    // 日志
    // ============================================================

    void LogDebug(string message)
    {
        if (Configuration.DebugMode)
        {
            logger.LogInformation("[文件管家] 🔍 {Message}", message);
        }
    }

    // ============================================================
    // 权限（黑白名单双模式）
    // ============================================================

    // 检查某群的某功能是否可用：先全局白名单，再单功能黑名单
    bool CheckFeaturePermission(long groupId, string feature)
    {
        string groupText = groupId.ToString();

        if (Configuration.PermissionMode == "allow_list")
        {
            List<string> allowedGroups = SplitList(Configuration.EnabledGroups);
            if (allowedGroups.Count == 0)
            {
                interactor.Poke("❌ 群文件插件当前为白名单模式且未配置允许的群号，所有群均已禁用，请联系管理员在插件配置中填写");
                return false;
            }
            if (allowedGroups.Contains(groupText) == false)
            {
                interactor.Poke("❌ 本群（" + groupText + "）不在群文件功能的允许名单中，无法使用该功能");
                return false;
            }
        }

        string disabledRaw = feature switch
        {
            "upload_file" => Configuration.DisabledUploadFile,
            "create_folder" => Configuration.DisabledCreateFolder,
            "delete_folder" => Configuration.DisabledDeleteFolder,
            "delete_file" => Configuration.DisabledDeleteFile,
            "move_file" => Configuration.DisabledMoveFile,
            "download_file" => Configuration.DisabledDownloadFile,
            "list_files" => Configuration.DisabledListFiles,
            "rename_file" => Configuration.DisabledRenameFile,
            "rename_folder" => Configuration.DisabledRenameFolder,
            _ => ""
        };
        List<string> disabledList = SplitList(disabledRaw);
        if (disabledList.Contains("all") || disabledList.Contains(groupText))
        {
            string featureName = feature switch
            {
                "upload_file" => "上传文件",
                "create_folder" => "创建文件夹",
                "delete_folder" => "删除文件夹",
                "delete_file" => "删除文件",
                "move_file" => "移动文件",
                "download_file" => "下载文件",
                "list_files" => "查看文件列表",
                "rename_file" => "重命名文件",
                "rename_folder" => "重命名文件夹",
                _ => feature
            };
            interactor.Poke("❌ " + featureName + "功能已在当前群禁用");
            return false;
        }
        return true;
    }

    static List<string> SplitList(string raw)
    {
        return raw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }

    // ============================================================
    // 群文件数据层
    // ============================================================

    // 获取根目录文件和文件夹列表
    async Task<(List<JsonElement> Files, List<JsonElement> Folders)> GetRootEntriesAsync(long groupId, int? fileCount = null)
    {
        List<JsonElement> files = new();
        List<JsonElement> folders = new();
        try
        {
            JsonObject parameters = new() { ["group_id"] = groupId };
            if (fileCount.HasValue)
            {
                parameters["file_count"] = fileCount.Value;
            }
            string response = await CallActionAsync("get_group_root_files", parameters);
            JsonElement data = ParseActionResponse(response, "获取群文件列表");
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
            LogDebug("获取到 " + files.Count + " 个文件, " + folders.Count + " 个文件夹");
        }
        catch (Exception listError)
        {
            logger.LogError("获取群文件列表异常：{Message}", listError.Message);
        }
        return (files, folders);
    }

    // 根目录写法三端不一致：LLOneBot 实测 folder_id 传空串回空列表（不是根目录），SnowLuma 用
    // folder_id || folder || "/"，NapCat 用 folder ?? folder_id ?? ""。只有 "/" 是三端都认的根目录。
    static string NormalizeFolderParam(string folderId)
    {
        string trimmed = (folderId ?? "").TrimStart('/');
        return trimmed.Length > 0 ? trimmed : "/";
    }

    // 获取文件夹内的文件，带5秒缓存与重试
    async Task<List<JsonElement>> GetFolderFilesAsync(long groupId, string folderId, int retryCount = 2, int? fileCount = null)
    {
        string cleanFolderId = NormalizeFolderParam(folderId);
        string cacheKey = groupId + "_" + cleanFolderId + "_" + (fileCount.HasValue ? fileCount.Value.ToString() : "default");
        if (folderCache.TryGetValue(cacheKey, out (DateTime CachedTime, List<JsonElement> Files) cached)
            && (DateTime.Now - cached.CachedTime).TotalSeconds < 5)
        {
            LogDebug("使用缓存: 文件夹 " + folderId + " 内有 " + cached.Files.Count + " 个文件");
            return cached.Files;
        }

        for (int attempt = 0; attempt <= retryCount; attempt++)
        {
            try
            {
                JsonObject parameters = new()
                {
                    ["group_id"] = groupId,
                    ["folder_id"] = cleanFolderId
                };
                if (fileCount.HasValue)
                {
                    parameters["file_count"] = fileCount.Value;
                }
                string response = await CallActionAsync("get_group_files_by_folder", parameters);
                JsonElement data = ParseActionResponse(response, "获取文件夹内容");
                List<JsonElement> files = new();
                if (data.ValueKind == JsonValueKind.Object && data.TryGetProperty("files", out JsonElement filesElement))
                {
                    files = filesElement.EnumerateArray().Select(item => item.Clone()).ToList();
                }
                else if (data.ValueKind == JsonValueKind.Array)
                {
                    files = data.EnumerateArray().Select(item => item.Clone()).ToList();
                }
                folderCache[cacheKey] = (DateTime.Now, files);
                LogDebug("文件夹 " + folderId + " 内有 " + files.Count + " 个文件");
                return files;
            }
            catch (Exception folderError)
            {
                LogDebug("获取文件夹内容失败(尝试 " + (attempt + 1) + "/" + (retryCount + 1) + ")：" + folderError.Message);
                if (attempt < retryCount)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(500 * (attempt + 1)));
                }
            }
        }
        return new List<JsonElement>();
    }

    void ClearCache(long groupId)
    {
        List<string> keysToRemove = folderCache.Keys
            .Where(key => key.StartsWith(groupId + "_", StringComparison.Ordinal))
            .ToList();
        foreach (string key in keysToRemove)
        {
            folderCache.Remove(key);
        }
        LogDebug("已清除群 " + groupId + " 的缓存");
    }

    static string GetEntryName(JsonElement entry, string fallback = "")
    {
        string name = GetStringField(entry, "file_name");
        if (name.Length == 0)
        {
            name = GetStringField(entry, "name");
        }
        if (name.Length == 0 && fallback.Length > 0)
        {
            name = fallback;
        }
        return name;
    }

    static string GetEntryFolderId(JsonElement folder)
    {
        string folderId = GetStringField(folder, "folder_id");
        if (folderId.Length == 0)
        {
            folderId = GetStringField(folder, "id");
        }
        if (folderId.Length == 0)
        {
            folderId = GetStringField(folder, "folder");
        }
        return folderId;
    }

    async Task<string> GetFolderIdByNameAsync(long groupId, string folderName)
    {
        (List<JsonElement> Files, List<JsonElement> Folders) root = await GetRootEntriesAsync(groupId);
        foreach (JsonElement folder in root.Folders)
        {
            string name = GetEntryName(folder);
            if (name == folderName)
            {
                return GetEntryFolderId(folder);
            }
        }
        return "";
    }

    async Task<string> GetFolderNameByIdAsync(long groupId, string folderId)
    {
        string target = (folderId ?? "").TrimStart('/');
        (List<JsonElement> Files, List<JsonElement> Folders) root = await GetRootEntriesAsync(groupId);
        foreach (JsonElement folder in root.Folders)
        {
            string candidate = GetEntryFolderId(folder);
            if (candidate == folderId || candidate.TrimStart('/') == target)
            {
                return GetEntryName(folder);
            }
        }
        return "";
    }

    // 文件条目：带位置信息的文件快照（供搜索/删除/重命名/移动使用）
    sealed class FileEntry
    {
        public JsonElement Source { get; set; }
        public string FileName { get; set; } = "";
        public string FileId { get; set; } = "";
        public string FileUuid { get; set; } = "";
        public long Busid { get; set; }
        public long Size { get; set; }
        public string Location { get; set; } = "";
        public string LocationFolderId { get; set; } = "";
    }

    static FileEntry ToFileEntry(JsonElement file, string location, string locationFolderId)
    {
        FileEntry entry = new()
        {
            Source = file.Clone(),
            FileName = GetStringField(file, "file_name").Length > 0 ? GetStringField(file, "file_name") : GetStringField(file, "name"),
            FileId = GetStringField(file, "file_id").Length > 0 ? GetStringField(file, "file_id") : GetStringField(file, "id"),
            FileUuid = GetStringField(file, "file_uuid").Length > 0 ? GetStringField(file, "file_uuid") : GetStringField(file, "file_id"),
            Busid = GetNumericField(file, "busid"),
            Size = GetNumericField(file, "file_size") > 0 ? GetNumericField(file, "file_size") : GetNumericField(file, "size"),
            Location = location,
            LocationFolderId = locationFolderId
        };
        return entry;
    }

    // 在指定文件夹（null=根目录）内按文件名精确查找一个文件
    async Task<FileEntry> FindFileInFolderAsync(long groupId, string fileName, string folderId)
    {
        List<JsonElement> files;
        if (string.IsNullOrEmpty(folderId) == false)
        {
            files = await GetFolderFilesAsync(groupId, folderId);
        }
        else
        {
            (List<JsonElement> Files, List<JsonElement> Folders) root = await GetRootEntriesAsync(groupId);
            files = root.Files;
            folderId = "/";
        }
        foreach (JsonElement file in files)
        {
            if (GetEntryName(file) == fileName)
            {
                FileEntry entry = ToFileEntry(file, "根目录", folderId ?? "/");
                if (folderId != "/" && string.IsNullOrEmpty(folderId) == false)
                {
                    string folderName = await GetFolderNameByIdAsync(groupId, folderId);
                    entry.Location = folderName.Length > 0 ? folderName : folderId;
                }
                LogDebug("找到匹配文件: " + fileName);
                return entry;
            }
        }
        return null;
    }

    // 在根目录及所有一级文件夹中按文件名查找（找到即返回）
    async Task<FileEntry> FindFileInAllFoldersAsync(long groupId, string fileName)
    {
        LogDebug("开始搜索文件: " + fileName);
        FileEntry rootMatch = await FindFileInFolderAsync(groupId, fileName, null);
        if (rootMatch != null)
        {
            return rootMatch;
        }
        (List<JsonElement> Files, List<JsonElement> Folders) root = await GetRootEntriesAsync(groupId);
        foreach (JsonElement folder in root.Folders)
        {
            string folderId = GetEntryFolderId(folder);
            if (folderId.Length == 0)
            {
                continue;
            }
            string folderName = GetEntryName(folder, "未知文件夹");
            FileEntry match = await FindFileInFolderAsync(groupId, fileName, folderId);
            if (match != null)
            {
                match.Location = folderName;
                match.LocationFolderId = folderId;
                return match;
            }
        }
        LogDebug("未找到文件: " + fileName);
        return null;
    }

    // 查找群内所有与文件名完全一致的文件（根目录+一级文件夹），用于删除/重命名的同名歧义校验
    async Task<List<FileEntry>> FindFileMatchesAllAsync(long groupId, string fileName)
    {
        List<FileEntry> matches = new();
        (List<JsonElement> Files, List<JsonElement> Folders) root = await GetRootEntriesAsync(groupId);
        foreach (JsonElement file in root.Files)
        {
            if (GetEntryName(file) == fileName)
            {
                matches.Add(ToFileEntry(file, "根目录", "/"));
            }
        }
        foreach (JsonElement folder in root.Folders)
        {
            string folderId = GetEntryFolderId(folder);
            if (folderId.Length == 0)
            {
                continue;
            }
            string folderName = GetEntryName(folder, folderId);
            List<JsonElement> folderFiles = await GetFolderFilesAsync(groupId, folderId);
            foreach (JsonElement file in folderFiles)
            {
                if (GetEntryName(file) == fileName)
                {
                    matches.Add(ToFileEntry(file, folderName, folderId));
                }
            }
        }
        LogDebug("精确查找「" + fileName + "」: 命中 " + matches.Count + " 处");
        return matches;
    }

    // 收集群文件并附带位置信息：folderId为null=根目录+全部一级文件夹；"/"=仅根目录；其他=该文件夹
    async Task<(List<FileEntry> Files, string Scope)> CollectGroupFilesAsync(long groupId, string folderId)
    {
        // 必须逐个顺序请求：NapCat/LLOneBot 对群文件接口的瞬时并发请求会失败/限流
        if (folderId == "/")
        {
            (List<JsonElement> Files, List<JsonElement> Folders) rootOnly = await GetRootEntriesAsync(groupId, SearchFileCount);
            List<FileEntry> rootEntries = rootOnly.Files
                .Select(file => ToFileEntry(file, "根目录", "/"))
                .ToList();
            return (rootEntries, "根目录");
        }

        if (string.IsNullOrEmpty(folderId) == false)
        {
            string folderName = await GetFolderNameByIdAsync(groupId, folderId);
            string scope = folderName.Length > 0 ? folderName : folderId;
            List<JsonElement> scopedFiles = await GetFolderFilesAsync(groupId, folderId, fileCount: SearchFileCount);
            List<FileEntry> scopedEntries = scopedFiles
                .Select(file => ToFileEntry(file, scope, folderId))
                .ToList();
            return (scopedEntries, "文件夹「" + scope + "」");
        }

        (List<JsonElement> Files, List<JsonElement> Folders) root = await GetRootEntriesAsync(groupId, SearchFileCount);
        List<FileEntry> collected = root.Files
            .Select(file => ToFileEntry(file, "根目录", "/"))
            .ToList();
        int folderIndex = 0;
        foreach (JsonElement folder in root.Folders)
        {
            folderIndex++;
            string currentFolderId = GetEntryFolderId(folder);
            if (currentFolderId.Length == 0)
            {
                LogDebug("跳过第 " + folderIndex + " 个文件夹：未能解析文件夹ID");
                continue;
            }
            string currentFolderName = GetEntryName(folder, currentFolderId);
            List<JsonElement> folderFiles = await GetFolderFilesAsync(groupId, currentFolderId, fileCount: SearchFileCount);
            if (folderFiles.Count == 0 && GetNumericField(folder, "total_file_count") > 0)
            {
                // 文件夹声称有文件却拉取为空，通常是请求失败/被限流而非真空文件夹
                logger.LogError("文件夹 {Name}（ID: {Id}）应有 {Count} 个文件，但拉取结果为空，可能被限流",
                    currentFolderName, currentFolderId, GetNumericField(folder, "total_file_count"));
            }
            collected.AddRange(folderFiles.Select(file => ToFileEntry(file, currentFolderName, currentFolderId)));
            LogDebug("遍历文件夹 " + folderIndex + "/" + root.Folders.Count + "：" + currentFolderName + " -> " + folderFiles.Count + " 个文件");
        }
        LogDebug("全量收集完成：共 " + collected.Count + " 个文件");
        return (collected, "根目录及全部文件夹");
    }

    // 按文件名关键词模糊匹配并排序：完全相同优先，其次关键词出现位置，再次字典序
    static List<FileEntry> MatchFilesByKeyword(List<FileEntry> files, string keyword)
    {
        string lowerKeyword = keyword.Trim().ToLowerInvariant();
        List<(int ExactFlag, int Position, string Name, FileEntry Entry)> scored = new();
        foreach (FileEntry file in files)
        {
            if (file.FileName.Length == 0)
            {
                continue;
            }
            string lowerName = file.FileName.ToLowerInvariant();
            int position = lowerName.IndexOf(lowerKeyword, StringComparison.Ordinal);
            if (position != -1)
            {
                scored.Add((lowerName == lowerKeyword ? 0 : 1, position, lowerName, file));
            }
        }
        return scored
            .OrderBy(item => item.ExactFlag)
            .ThenBy(item => item.Position)
            .ThenBy(item => item.Name)
            .Select(item => item.Entry)
            .ToList();
    }

    // ============================================================
    // 协议端自适应（NapCat / LLOneBot / SnowLuma 参数格式差异）
    // ============================================================

    // 启动后识别一次协议端：三端都回 app_name，go-cqhttp 系才回 name/vendor，两种都取
    void StartProviderCheck()
    {
        Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), DestroyCancellationToken);
                string response = await CallActionAsync("get_version_info", new JsonObject());
                JsonElement data = ParseActionResponse(response, "获取协议端版本");
                providerName = (GetStringField(data, "app_name") + " " + GetStringField(data, "name")
                    + " " + GetStringField(data, "vendor")).Trim();
                logger.LogInformation("[文件管家] 协议端：{Provider} {Version}",
                    providerName.Length > 0 ? providerName : "未识别", GetStringField(data, "app_version"));
                if (apiProvider == "auto")
                {
                    apiProvider = AnchorProviderOf(providerName);
                }
            }
            catch (Exception versionError)
            {
                logger.LogInformation("[文件管家] 未能识别协议端：{Message}", versionError.Message);
            }
        }, DestroyCancellationToken);
    }

    static string AnchorProviderOf(string identifiedName)
    {
        string lower = identifiedName.ToLowerInvariant();
        if (lower.Contains("napcat"))
        {
            return "napcat";
        }
        if (lower.Contains("llonebot"))
        {
            return "llonebot";
        }
        if (lower.Contains("snowluma"))
        {
            return "snowluma";
        }
        return "auto";
    }

    // NapCat 的 file_id 是它自己内存里编码的临时标识（约24小时过期、NapCat 重启即失效），
    // 拿旧 id 操作时它抛 'real fileUUID not found!'，不解释一句看起来像插件坏了
    static string ExplainFileIdFailure(string message)
    {
        if (message.Contains("fileUUID", StringComparison.OrdinalIgnoreCase) == false)
        {
            return message;
        }
        return message + "（NapCat 的 file_id 是协议端内存里的临时标识，重新搜索文件拿最新 id 再试）";
    }

    // 移动文件：NapCat 用 current/target_parent_directory，LLOneBot 与 SnowLuma 用 parent/target_directory。
    // 认得出协议端就只发对的那一种，认不出时两种依次试并把试出来的格式记下来。
    async Task<(bool Success, string Message)> MoveFileAsync(
        long groupId, string fileUuid, string currentFolderId, string targetFolderId, string fileName)
    {
        string cleanCurrent = NormalizeFolderParam(currentFolderId);
        string cleanTarget = NormalizeFolderParam(targetFolderId);

        // 已经试出来的格式不再浪费一次失败；否则按协议端排首选，两种都留一次机会
        // （配置填错端时也能靠回退成功，不至于把移动功能锁死）
        List<string> styles = moveParameterStyle.Length > 0
            ? new List<string> { moveParameterStyle }
            : apiProvider == "napcat"
                ? new List<string> { "napcat", "onebot" }
                : apiProvider is "llonebot" or "snowluma"
                    ? new List<string> { "onebot", "napcat" }
                    : new List<string> { "napcat", "onebot" };

        string lastMessage = "";
        foreach (string style in styles)
        {
            JsonObject parameters = style == "onebot"
                ? new JsonObject
                {
                    ["group_id"] = groupId,
                    ["file_id"] = fileUuid,
                    ["parent_directory"] = cleanCurrent,
                    ["target_directory"] = cleanTarget
                }
                : new JsonObject
                {
                    ["group_id"] = groupId,
                    ["file_id"] = fileUuid,
                    ["current_parent_directory"] = cleanCurrent,
                    ["target_parent_directory"] = cleanTarget
                };
            try
            {
                LogDebug("尝试 " + style + " 格式移动文件: " + fileName);
                string response = await CallActionAsync("move_group_file", parameters);
                ParseActionResponse(response, "移动文件");
                if (moveParameterStyle != style)
                {
                    moveParameterStyle = style;
                    logger.LogInformation("[文件管家] 移动接口参数格式：{Style}", style);
                }
                return (true, "");
            }
            catch (Exception moveError)
            {
                lastMessage = moveError.Message;
            }
        }
        return (false, ExplainFileIdFailure(lastMessage));
    }

    // ============================================================
}
