using System.Diagnostics;
using System.Runtime.InteropServices;
using Traymote.Core;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Traymote.Services;

/// <summary>A message-only window receives the global shortcut without polling or keeping a flyout alive.</summary>
internal sealed unsafe class GlobalHotKeyService : IDisposable
{
    private const uint HotKeyMessage = 0x0312;
    private const int HotKeyId = 1;
    private const string WindowClassName = "Traymote_HotKeyWindow";
    private static readonly HWND MessageOnlyParent = new(-3); // HWND_MESSAGE

    private readonly Action _pressed;
    private readonly WNDPROC _wndProc; // Native callbacks must not outlive the delegate.
    private HWND _hwnd;
    private bool _isClassRegistered;

    public GlobalHotKeyService(Action pressed)
    {
        _pressed = pressed;
        _wndProc = WndProc;
    }

    public bool IsRegistered { get; private set; }

    public int LastError { get; private set; }

    public void Register(HotKey hotKey)
    {
        if (!hotKey.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(hotKey));
        }

        Unregister();
        LastError = 0;
        if (!EnsureWindow())
        {
            return;
        }

        // Holding the shortcut must not repeatedly toggle the remote.
        const uint ModNoRepeat = 0x4000;
        IsRegistered = PInvoke.RegisterHotKey(_hwnd, HotKeyId,
            (HOT_KEY_MODIFIERS)(hotKey.Modifiers | ModNoRepeat), hotKey.VirtualKey);
        if (!IsRegistered)
        {
            RecordError("RegisterHotKey");
        }
    }

    public void Unregister()
    {
        if (IsRegistered)
        {
            if (!PInvoke.UnregisterHotKey(_hwnd, HotKeyId))
            {
                RecordError("UnregisterHotKey");
            }

            IsRegistered = false;
        }
    }

    public void Dispose()
    {
        Unregister();
        if (_hwnd != HWND.Null)
        {
            if (!PInvoke.DestroyWindow(_hwnd))
            {
                RecordError("DestroyWindow");
                return;
            }

            _hwnd = HWND.Null;
        }

        if (_isClassRegistered)
        {
            if (!PInvoke.UnregisterClass(WindowClassName, null))
            {
                RecordError("UnregisterClass");
            }

            _isClassRegistered = false;
        }
    }

    private bool EnsureWindow()
    {
        if (_hwnd != HWND.Null)
        {
            return true;
        }

        if (!_isClassRegistered)
        {
            fixed (char* className = WindowClassName)
            {
                WNDCLASSW windowClass = new()
                {
                    lpfnWndProc = _wndProc,
                    lpszClassName = className,
                };
                _isClassRegistered = PInvoke.RegisterClass(windowClass) != 0;
            }

            if (!_isClassRegistered)
            {
                RecordError("RegisterClass");
                return false;
            }
        }

        _hwnd = PInvoke.CreateWindowEx(0, WindowClassName, null, 0,
            0, 0, 0, 0, MessageOnlyParent, null, null, null);
        if (_hwnd == HWND.Null)
        {
            RecordError("CreateWindowEx");
            return false;
        }

        return true;
    }

    private void RecordError(string operation)
    {
        LastError = Marshal.GetLastPInvokeError();
        Debug.WriteLine($"[HotKey] {operation} failed: Win32 error {LastError}.");
    }

    private LRESULT WndProc(HWND hwnd, uint message, WPARAM wParam, LPARAM lParam)
    {
        if (message == HotKeyMessage && (int)wParam.Value == HotKeyId)
        {
            if (IsRegistered)
            {
                _pressed();
            }

            return new LRESULT(0);
        }

        return PInvoke.DefWindowProc(hwnd, message, wParam, lParam);
    }
}
