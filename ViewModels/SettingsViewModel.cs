using System.Windows.Input;
using ImageTool.Models;
using ImageTool.Services;

namespace ImageTool.ViewModels;

/// <summary>
/// 设置页视图模型：开机自启开关、全局热键录制、默认保存目录、保存。
/// </summary>
public class SettingsViewModel : ViewModelBase
{
    private readonly AppSettings _settings = SettingsStore.Load();
    private bool _autoStart;
    private string _hotkeyText = "";
    private bool _recording;
    private int _modifiers;
    private int _key;
    private string _saveDir = "";
    private string _status = "";

    public SettingsViewModel()
    {
        _autoStart = _settings.AutoStart;
        _modifiers = _settings.HotkeyModifiers;
        _key = _settings.HotkeyKey;
        _saveDir = _settings.DefaultSaveDir;
        UpdateHotkeyText();
        SaveCommand = new RelayCommand(Save);
    }

    public ICommand SaveCommand { get; }

    public bool AutoStart
    {
        get => _autoStart;
        set
        {
            if (Set(ref _autoStart, value))
            {
                // 即时同步注册表，体验更顺滑
                if (_autoStart) StartupManager.Enable();
                else StartupManager.Disable();
                Status = _autoStart ? "已开启开机自启" : "已关闭开机自启";
            }
        }
    }

    public string HotkeyText => _hotkeyText;

    public bool Recording
    {
        get => _recording;
        set => Set(ref _recording, value);
    }

    public string SaveDir
    {
        get => _saveDir;
        set => Set(ref _saveDir, value);
    }

    public string Status
    {
        get => _status;
        set => Set(ref _status, value);
    }

    /// <summary>由设置页录制逻辑调用：写入新热键、刷新显示，并立即持久化 + 重注册</summary>
    public void SetHotkey(int modifiers, int key)
    {
        _modifiers = modifiers;
        _key = key;
        UpdateHotkeyText();

        // 录制完成即生效，避免“录了但没保存就以为没更新”
        _settings.HotkeyModifiers = modifiers;
        _settings.HotkeyKey = key;
        SettingsStore.Save(_settings);
        try { ((App)System.Windows.Application.Current).ReregisterHotkey(modifiers, key); }
        catch { /* 注册失败已有热键仍可用，忽略 */ }
        Status = "热键已更新并立即生效";
    }

    private void UpdateHotkeyText()
    {
        _hotkeyText = HotkeyFormatter.Format(_modifiers, _key);
        OnPropertyChanged(nameof(HotkeyText));
    }

    private void Save()
    {
        _settings.AutoStart = _autoStart;
        _settings.HotkeyModifiers = _modifiers;
        _settings.HotkeyKey = _key;
        _settings.DefaultSaveDir = _saveDir;
        SettingsStore.Save(_settings);

        // 重新注册热键，使新组合立即生效
        ((App)System.Windows.Application.Current).ReregisterHotkey(_modifiers, _key);
        Status = "设置已保存";
    }
}
