using System.Windows;

namespace OpenIsland;

/// <summary>Значок в системном трее: показать/скрыть остров, настройки, выход.</summary>
public partial class MainWindow
{
    const uint TrayId = 1;
    IntPtr _trayIcon = IntPtr.Zero;
    bool _trayAdded;

    void InitTray()
    {
        if (_trayAdded) return;
        _trayIcon = Native.LoadSmallAppIcon();
        _trayAdded = Native.AddTrayIcon(_hwnd, TrayId, _trayIcon, "OpenIsland");
        _windowSource?.AddHook(OnTrayMessage);
        App.LogDebug(_trayAdded ? "Tray icon created." : "Tray icon could not be created.");
    }

    void DisposeTray()
    {
        _windowSource?.RemoveHook(OnTrayMessage);
        if (_trayAdded)
        {
            Native.RemoveTrayIcon(_hwnd, TrayId);
            _trayAdded = false;
        }
        Native.FreeIcon(_trayIcon);
        _trayIcon = IntPtr.Zero;
    }

    IntPtr OnTrayMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != Native.TrayCallback) return IntPtr.Zero;
        handled = true;
        switch ((int)(lParam.ToInt64() & 0xFFFF))
        {
            case Native.WM_LBUTTONDBLCLK:
                RevealIsland();
                break;
            case Native.WM_RBUTTONUP:
            case Native.WM_CONTEXTMENU:
                ShowTrayMenu();
                break;
        }
        return IntPtr.Zero;
    }

    void ShowTrayMenu()
    {
        // Меню трея требует foreground-окна; оверлей по умолчанию не активируем.
        bool wasActivatable = _activatable;
        MakeActivatable(true);
        IntPtr menu = Native.NewMenu();
        try
        {
            Native.MenuItem(menu, 1, "Открыть остров");
            Native.MenuItem(menu, 2, IsVisible ? "Скрыть остров" : "Показать остров");
            Native.MenuItem(menu, 3, "Настройки");
            Native.MenuSeparator(menu);
            Native.MenuItem(menu, 4, "Выход");
            switch (Native.ShowTrayMenu(_hwnd, menu))
            {
                case 1: RevealIsland(); break;
                case 2: ToggleIsland(); break;
                case 3: RevealSettings(); break;
                case 4: Exit(); break;
            }
        }
        finally
        {
            Native.DeleteMenu(menu);
            MakeActivatable(wasActivatable);
        }
    }

    void ToggleIsland() => SetIslandShown(!IsVisible);

    void RevealIsland() => SetIslandShown(true);

    void RevealSettings()
    {
        SetIslandShown(true);
        ShowPanel(Panel.Settings);
    }

    void SetIslandShown(bool shown)
    {
        if (shown)
        {
            Show();
            SyncGlass(false);
            StayOnTop();
        }
        else
        {
            CollapseNow();
            // Стекло — отдельное окно-подложка; его нужно убрать вместе с основным.
            _glass.Hide();
            Hide();
        }
    }
}
