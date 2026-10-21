
# Suran.GroupFileManager

QQ 群文件管家，移植自 KiraAI 的 qq_file_manager，功能对齐：文件列表/搜索/重命名/文件夹管理/删除/移动/异步下载。所有操作通过 OneBot 正向 WebSocket 直发协议端（常驻接收循环 + 断线重连），自动适配 NapCat、LLOneBot 与 SnowLuma 的接口差异。

## 版本
4.1.1（对应 Alife 客户端 4.2.x）

## 依赖
- Alife.Function.FunctionCaller

## 配置
| 键 | 默认 | 说明 |
|---|---|---|
| OneBotWsUrl | ws://127.0.0.1:3001 | OneBot 正向 WebSocket 地址 |
| MaxFilesList | 20 | 列出/搜索时最多显示条数 |
| DebugMode | 关 | 调试日志（API调用/搜索/缓存过程） |
| ApiProvider | auto | 协议端类型：auto 自动识别 / napcat / llonebot / snowluma（影响移动文件接口参数格式） |
| DownloadPath | files | 下载保存目录，相对存储目录（禁止..和盘符），即 {存储目录}/files |
| MaxFileSizeMb | 100 | 下载大小上限，0不限制 |
| AllowedExtensions | 空 | 下载扩展名白名单，逗号分隔，留空允许全部 |
| DownloadTimeoutSeconds | 60 | 下载超时 |
| PermissionMode | deny_list | deny_list 黑名单（默认）/ allow_list 白名单 |
| EnabledGroups | 空 | 白名单模式的允许群号（逗号分隔），留空=全部禁用 |
| DisabledCreateFolder / DisabledDeleteFolder / DisabledDeleteFile / DisabledMoveFile / DisabledRenameFile / DisabledRenameFolder / DisabledDownloadFile / DisabledListFiles | 空 | 各功能禁用群号列表（逗号分隔，填 all 全局禁用） |

## AI 函数（11 个）
- ListGroupFiles(groupId) - 根目录文件和文件夹列表
- ListFolderFiles(groupId, folderId, folderName) - 指定文件夹内容，两种定位二选一
- SearchGroupFiles(groupId, keyword, folderId, folderName) - 关键词搜索（默认根目录+全部文件夹，结果带file_id/位置/大小/上传者/日期），folderName填'根目录'仅搜根目录
- CreateFolder(groupId, folderName) - 创建文件夹（已存在会提示）
- DeleteFolder(groupId, folderName, folderId) - 删除空文件夹
- DeleteGroupFiles(groupId, fileIds, fileNames) - 删除文件（批量），fileIds/fileNames用英文逗号分隔；按名删除时同名多处会拒绝并列出位置，防止连坐误删
- MoveGroupFile(groupId, fileName, folderName) - 移动文件，folderName填'根目录'或'/'到根目录
- RenameGroupFile(groupId, newName, fileId, fileName) - 重命名文件（三端都支持）
- RenameGroupFolder(groupId, newFolderName, folderId, folderName) - 重命名文件夹（仅 LLOneBot/SnowLuma，NapCat 无此接口）
- DownloadGroupFile(groupId, fileName) - 异步下载到 {存储目录}/下载目录，完成后主动通知AI
- CheckDownloadTask(taskId) - 查询下载任务状态

## 权限模型（与原版一致）
- 黑名单模式（默认）：所有群可用，各功能独立禁用列表微调，填 all 全局禁用
- 白名单模式：仅 EnabledGroups 内的群可用（留空=全部禁用），名单内群仍受单功能黑名单约束

## 性能与兼容（继承自原版）
- 文件夹内容缓存 5 秒，写操作后主动清除
- 文件夹内容拉取失败自动重试（2次，退避0.5s）
- 搜索/全量收集按文件夹顺序逐个请求（NapCat/LLOneBot 并发限流规避），文件夹声称有文件却拉空时日志告警
- 移动文件按协议端排参数格式首选、另一种兜底，实测生效的格式会记住；重命名按三端源码直接用固定参数
- 下载双重校验：创建任务前按扩展名/文件大小预检，下载中按 Content-Length 二次校验
- 文件名非法字符清洗、重名自动加时间戳

## 三端兼容（NapCat / LLOneBot / SnowLuma）
接口差异全部对着三端源码核对，不是猜的：
- 启动后 `get_version_info` 识别协议端（三端都回 `app_name`），日志署名并在 retcode 1404 时提示「X 未实现该接口」
- **移动文件**参数名分两种：NapCat `current_parent_directory`/`target_parent_directory`，LLOneBot 与 SnowLuma 同为 `parent_directory`/`target_directory`。按识别出的端排首选、另一种兜底（配置填错端也能靠回退成功），实测生效的格式记住后不再浪费一次失败
- **文件夹重命名**动作名是 `rename_group_file_folder`（此前用的 `rename_group_folder` 三端都不存在，等于从来没生效过），NapCat 未实现该接口，会明确报「X 未实现该接口」
- **文件重命名**三端都支持，参数固定 `file_id` + `current_parent_directory` + `new_name`（不再盲试 `new_file_name` 这类不存在的变体）
- SnowLuma 的删除文件夹注册名是 `delete_group_file_folder`，`delete_group_folder` 是它的别名，现名可用
- `delete_group_file` 只发 `group_id` + `file_id` 就能在三端通过（SnowLuma 的 `busid` 是 `f.raw()` 可选，缺省按 102 处理）
- **下载链接必须带上文件条目自己的 `busid`**：同一个群里 102 和 104 都真实出现过，SnowLuma 缺省时钉死 102，拿 104 的文件去下载就会失败；条目里没有才回落 102
- **列目录/移动/重命名时的根目录一律送 `"/"`**：三端写法不一致——LLOneBot 实测 `folder_id` 传空串回**空列表**（不是根目录），传 `"/"` 才是根目录；SnowLuma 用 `folder_id || folder || "/"`，且它的 `move_group_file` 两个目录字段是非空字符串，传空会 retcode 1400；NapCat 读 `folder ?? folder_id ?? ''`。插件统一 `NormalizeFolderParam`（剥前导斜杠、空则 `"/"`），LLOneBot 那种带前导斜杠的 id 也能用
- 失败原因同时读 `message` 与 `wording`：SnowLuma 的失败包只填 `wording`，只读 `message` 会让它所有报错都显示成「未知错误」，连 `folder_id is required` 这种参数级原因都被丢掉
- 文件/文件夹定位字段两端不一致（NapCat 文件夹回 `folder`，其余回 `folder_id`；名称 `name` / `file_name`），统一走 `GetEntryFolderId` / `GetEntryName` 兜底
- NapCat 的 `file_id` 是它自己内存编码的临时标识（约 24 小时、NapCat 重启即失效），拿旧 id 操作会报 `real fileUUID not found!`，现在会把「重新搜索拿最新 id」一并提示；LLOneBot 与 SnowLuma 用 NTQQ 原生 fileId，跨端稳定
- 删除文件失败逐条带原因汇报，不再只报「N 个删除失败」

## 与 KiraAI 原版的差异
- 群号改为函数参数显式传入（Alife 的 XmlFunction 无会话上下文），AI 从 QChat 群聊上下文取值
- 下载目录挂在 {存储目录} 下（原版挂在 KiraAI 的 data/ 下）；下载历史不持久化（原版存 plugin_data）
- 下载完成通知通过 Poke 推给AI（原版发回来源会话）
- file_ids/file_names 数组参数改为英文逗号分隔的字符串（XmlFunction 参数需可从字符串转换）
