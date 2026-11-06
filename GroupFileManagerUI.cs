using Alife.Framework;
using Microsoft.AspNetCore.Components.Rendering;

namespace Suran.GroupFileManager;

/// <summary>群文件管家 的设置页。内容由 SuranUiKit 按配置项自动分组生成。</summary>
public sealed class GroupFileManagerUI : ModuleUIBase<GroupFileManagerModule, GroupFileManagerConfig>
{
    protected override void BuildRenderTree(RenderTreeBuilder b)
    {
        if (Configuration == null)
        {
            b.AddContent(0, "配置未加载");
            return;
        }
        SuranUiKit.Render(this, StateHasChanged, b, Configuration, "群文件管家", "群文件列表、搜索、重命名、删除、移动与下载；黑白名单双权限", "#0ea5e9");
    }
}
