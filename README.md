# Suran.GroupFileManager

群文件管理：列目录、搜索、上传下载、移动重命名，还能按规则自动整理。

## 功能

- 浏览：列群文件根目录、展开某个文件夹、按关键词搜索
- 目录：新建、删除、重命名
- 文件：上传单个或批量、下载、删除、移动、重命名
- 在线预览文件内容，不用先下载
- 下载任务：查看进度与结果
- 统计：文件数、大小分布；树状视图看整体结构
- 自动整理：按规则把文件归到对应文件夹

## 安装

先装依赖：`Alife.Function.FunctionCaller`。

装法二选一：插件市场里搜 `Suran.GroupFileManager`；或把目录放到 `Storage/Plugins/Suran.GroupFileManager/`，重启角色生效。

## 配置

在设置页里改，改完重启角色生效。

| 设置项 | 键 | 说明 | 默认 |
| --- | --- | --- | --- |
| 运行·功能文档常驻 | `DocumentAlwaysResident` | 开：功能说明常驻系统提示词，模型随时看得到工具；关：隐式加载省 token，模型先输出入口标签再读文档。群文件操作多是主人明确要求时用，默认关 | `false` |
| 基础·协议端地址 | `OneBotWsUrl` | 留空 = 自动跟随「QQ 聊天」插件的配置（推荐）。填了就用这里的，例如 ws://127.0.0.1:3001 | `""` |
| 基础·访问令牌 | `OneBotToken` | 与协议端配置的 token 一致。留空 = 跟随「QQ 聊天」插件。填了地址却没填这里，协议端一开 token 就会连不上 | `""` |
| 基础·列表最大条数 | `MaxFilesList` | 列出/搜索文件时最多显示的条目数量 | `20` |
| 基础·调试模式 | `DebugMode` | 开启后日志输出API调用、搜索、缓存等详细过程 | `false` |
| 基础·协议端类型 | `ApiProvider` | 协议端类型，auto 自动探测，也可指定 napcat / llonebot / snowluma。 | `"auto"` |
| 下载·保存目录 | `DownloadPath` | 相对存储目录的相对路径（禁止..和盘符），默认 files，即 {存储目录}/files | `"files"` |
| 下载·大小上限(MB) | `MaxFileSizeMb` | 允许下载的最大文件大小，0为不限制 | `100` |
| 下载·扩展名白名单 | `AllowedExtensions` | 允许下载的扩展名，英文逗号分隔（如 pdf,jpg,zip），留空允许所有类型 | `""` |
| 下载·超时(秒) | `DownloadTimeoutSeconds` | 下载超时时间 | `60` |
| 上传·大小上限(MB) | `MaxUploadSizeMb` | 允许上传到群文件的本地文件最大体积，0为不限制，默认200 | `200` |
| 上传·扩展名白名单 | `UploadAllowedExtensions` | 允许上传的扩展名，英文逗号分隔（如 pdf,jpg,zip），留空允许所有类型 | `""` |
| 预览·最大 KB | `PreviewMaxKb` | 在线预览群文件时最多读取的大小（KB），默认512。超出部分不读取，仅支持文本类文件 | `512` |
| 归档·规则 | `ArchiveRules` | 自动归档规则，格式 扩展名=文件夹名，多条用英文分号分隔，例如 pdf,docx=文档;jpg,png=图片;zip,7z=压缩包。留空则自动归档功能不可用 | `""` |
| 权限·权限模式 | `PermissionMode` | deny_list=黑名单（默认，所有群可用按需禁用）/ allow_list=白名单（仅允许名单内的群） | `"deny_list"` |
| 权限·白名单群号 | `EnabledGroups` | 白名单模式下允许使用群文件功能的群号，英文逗号分隔；留空等于全部禁用 | `""` |
| 权限·禁用创建文件夹的群 | `DisabledCreateFolder` | 群号英文逗号分隔，填 all 全局禁用，留空不限制 | `""` |
| 权限·禁用删除文件夹的群 | `DisabledDeleteFolder` | 群号英文逗号分隔，填 all 全局禁用，留空不限制 | `""` |
| 权限·禁用删除文件的群 | `DisabledDeleteFile` | 群号英文逗号分隔，填 all 全局禁用，留空不限制 | `""` |
| 权限·禁用移动文件的群 | `DisabledMoveFile` | 群号英文逗号分隔，填 all 全局禁用，留空不限制 | `""` |
| 权限·禁用重命名文件的群 | `DisabledRenameFile` | 群号英文逗号分隔，填 all 全局禁用，留空不限制 | `""` |
| 权限·禁用重命名文件夹的群 | `DisabledRenameFolder` | 群号英文逗号分隔，填 all 全局禁用，留空不限制 | `""` |
| 权限·禁用下载文件的群 | `DisabledDownloadFile` | 群号英文逗号分隔，填 all 全局禁用，留空不限制 | `""` |
| 权限·禁用查看列表的群 | `DisabledListFiles` | 群号英文逗号分隔，填 all 全局禁用；同时控制搜索功能 | `""` |
| 权限·禁用上传的群 | `DisabledUploadFile` | 群号英文逗号分隔，填 all 全局禁用，留空不限制 | `""` |
