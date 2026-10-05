
# Suran.GroupFileManager

QQ 群文件管家，移植自 KiraAI 的 qq_file_manager，功能对齐：文件列表/搜索/重命名/文件夹管理/删除/移动/异步下载。所有操作通过 OneBot 正向 WebSocket 直发协议端（常驻接收循环 + 断线重连），自动适配 NapCat 与 LLOneBot 的接口差异。

## 版本
4.0.0（对应 Alife 客户端 4.2.x）

## 依赖
- Alife.Function.FunctionCaller

## 配置
| 键 | 默认 | 说明 |
|---|---|---|
| OneBotWsUrl | ws://127.0.0.1:3001 | OneBot 正向 WebSocket 地址 |
| MaxFilesList | 20 | 列出/搜索时最多显示条数 |
| DebugMode | 关 | 调试日志（API调用/搜索/缓存过程） |
| ApiProvider | auto | 协议端类型：auto 自动探测 / napcat / llonebot（影响移动与重命名接口格式） |
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
- RenameGroupFile(groupId, newName, fileId, fileName) - 重命名文件（NapCat 支持）
- RenameGroupFolder(groupId, newFolderName, folderId, folderName) - 重命名文件夹（LLOneBot 支持）
- DownloadGroupFile(groupId, fileName) - 异步下载到 {存储目录}/下载目录，完成后主动通知AI
- CheckDownloadTask(taskId) - 查询下载任务状态

## 权限模型（与原版一致）
- 黑名单模式（默认）：所有群可用，各功能独立禁用列表微调，填 all 全局禁用
- 白名单模式：仅 EnabledGroups 内的群可用（留空=全部禁用），名单内群仍受单功能黑名单约束

## 性能与兼容（继承自原版）
- 文件夹内容缓存 5 秒，写操作后主动清除
- 文件夹内容拉取失败自动重试（2次，退避0.5s）
- 搜索/全量收集按文件夹顺序逐个请求（NapCat/LLOneBot 并发限流规避），文件夹声称有文件却拉空时日志告警
- 移动文件自动探测 NapCat/LLOneBot 参数格式并记忆；重命名接口多参数变体依次尝试
- 下载双重校验：创建任务前按扩展名/文件大小预检，下载中按 Content-Length 二次校验
- 文件名非法字符清洗、重名自动加时间戳

## 与 KiraAI 原版的差异
- 群号改为函数参数显式传入（Alife 的 XmlFunction 无会话上下文），AI 从 QChat 群聊上下文取值
- 下载目录挂在 {存储目录} 下（原版挂在 KiraAI 的 data/ 下）；下载历史不持久化（原版存 plugin_data）
- 下载完成通知通过 Poke 推给AI（原版发回来源会话）
- file_ids/file_names 数组参数改为英文逗号分隔的字符串（XmlFunction 参数需可从字符串转换）
