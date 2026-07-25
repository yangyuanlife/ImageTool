using System.Collections.ObjectModel;
using ImageTool.Models;
using ImageTool.Services;

namespace ImageTool.ViewModels;

/// <summary>
/// 主视图模型：聚合功能页 + 侧边栏导航状态
/// </summary>
public class MainViewModel : ViewModelBase
{
    public ResizeViewModel ResizeVm { get; }
    public ConvertViewModel ConvertVm { get; }
    public CompressViewModel CompressVm { get; }
    public SettingsViewModel SettingsVm { get; }

    /// <summary>侧边栏导航项（顺序需与内容区各 View 的索引一致）</summary>
    public ObservableCollection<NavItem> NavItems { get; } = new()
    {
        new NavItem { Name = "调整大小", IconPath = "M4 4 L12 12 M4 4 L7 4 M4 4 L4 7 M12 12 L9 12 M12 12 L12 9" },
        new NavItem { Name = "格式转换", IconPath = "M3 6 H13 M10 3 L13 6 L10 9 M13 10 H3 M6 13 L3 10 L6 7" },
        new NavItem { Name = "压缩", IconPath = "M8 3 V13 M4 9 L8 13 L12 9 M3 14 H13" },
        new NavItem { Name = "设置", IconPath = "M8 5 A3 3 0 1 0 8 11 A3 3 0 1 0 8 5 M8 2 V4 M8 12 V14 M2 8 H4 M12 8 H14" }
    };

    private int _selectedIndex;
    /// <summary>当前选中的功能页索引（0=调整大小 … 3=设置）</summary>
    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (Set(ref _selectedIndex, value))
                OnPropertyChanged(nameof(CurrentPage)); // 切页时同步通知 CurrentPage
        }
    }

    /// <summary>
    /// 当前应显示的功能页 ViewModel。由 ContentControl + 隐式 DataTemplate 自动映射成对应视图。
    /// 同一时刻只有一页在视觉树中，从结构上杜绝「多页堆叠」问题。
    /// </summary>
    public object CurrentPage => _selectedIndex switch
    {
        0 => ResizeVm,
        1 => ConvertVm,
        2 => CompressVm,
        _ => SettingsVm
    };

    public MainViewModel(IImageService service)
    {
        ResizeVm = new ResizeViewModel(service);
        ConvertVm = new ConvertViewModel(service);
        CompressVm = new CompressViewModel(service);
        SettingsVm = new SettingsViewModel();

        // 底部「截图」按钮下方的快捷键提示：直接读设置作为初始值，避免依赖 App.OnStartup 的赋值时机
        // （MainViewModel 构造早于 App.CurrentHotkeyHint 的初始化，否则首次打开会是空串）。
        var s = SettingsStore.Load();
        _screenshotHotkeyHint = HotkeyFormatter.Format(s.HotkeyModifiers, s.HotkeyKey);
        App.HotkeyChanged += () => ScreenshotHotkeyHint = App.CurrentHotkeyHint;
    }

    private string _screenshotHotkeyHint = "";
    /// <summary>主窗口底部显示的截图快捷键提示（动态同步设置页的变更）</summary>
    public string ScreenshotHotkeyHint
    {
        get => _screenshotHotkeyHint;
        set => Set(ref _screenshotHotkeyHint, value);
    }
}
