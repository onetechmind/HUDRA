using System;
using System.Collections.Generic;
using System.Windows.Forms;
using Gma.System.MouseKeyHook;
using HandheldCompanion.OpenLibSys;

namespace HUDRA.Services
{
    public class TurboService : IDisposable
    {
        private readonly IKeyboardMouseEvents _hook;
        private readonly HashSet<Keys> _pressed = new();
        private readonly OpenLibSys _ec;

        public TurboService()
        {
            _ec = new OpenLibSys();
            WriteEc(_ec, 0x4EB, 0x40);

            _hook = Hook.GlobalEvents();
            _hook.KeyDown += OnKeyDown;
            _hook.KeyUp += OnKeyUp;
        }

        private void OnKeyDown(object? sender, KeyEventArgs e)
        {
            _pressed.Add(e.KeyCode);
            if (_pressed.Contains(Keys.LWin) &&
                _pressed.Contains(Keys.LMenu) &&
                _pressed.Contains(Keys.RControlKey))
            {
                Console.WriteLine("Turbo button pressed");
            }
        }

        private void OnKeyUp(object? sender, KeyEventArgs e)
        {
            _pressed.Remove(e.KeyCode);
        }

        private static void WriteEc(OpenLibSys ols, ushort addr, byte data)
        {
            ols.WriteIoPortByte(0x2E, 0x11);
            ols.WriteIoPortByte(0x2F, (byte)(addr >> 8));
            ols.WriteIoPortByte(0x2E, 0x10);
            ols.WriteIoPortByte(0x2F, (byte)(addr & 0xFF));
            ols.WriteIoPortByte(0x2E, 0x12);
            ols.WriteIoPortByte(0x2F, data);
        }

        public void Dispose()
        {
            _hook.KeyDown -= OnKeyDown;
            _hook.KeyUp -= OnKeyUp;
            _hook.Dispose();
            _ec.Dispose();
        }
    }
}
