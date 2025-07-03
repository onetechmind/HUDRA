using Gma.System.MouseKeyHook;
using OpenLibSys;
using System;
using System.Collections.Generic;
using System.Drawing.Text;
using System.Net;
using System.Windows.Forms;
using static System.Runtime.InteropServices.JavaScript.JSType;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.ListView;

namespace HUDRA.Services
{
    public class TurboService : IDisposable
    {
        private readonly IKeyboardMouseEvents _hook;
        private readonly HashSet<Keys> _pressed = new();
        private readonly OpenLibSys.Ols _ec;

        public event EventHandler? TurboButtonPressed;
        public TurboService()
        {
            try
            {
                byte addr_upper = (byte)((0x4EB >> 8) & byte.MaxValue);
                byte addr_lower = (byte)(0x4EB & byte.MaxValue);

                _ec = new Ols();

                if (_ec.GetStatus() != (uint)Ols.Status.NO_ERROR)
                {
                    throw new InvalidOperationException("Failed to initialize OpenLibSys");
                }

                _ec.WriteIoPortByte(0x4E, 0x2E);
                _ec.WriteIoPortByte(0x4F, 0x11);
                _ec.WriteIoPortByte(0x4E, 0x2F);
                _ec.WriteIoPortByte(0x4F, addr_upper);
                _ec.WriteIoPortByte(0x4E, 0x2E);
                _ec.WriteIoPortByte(0x4F, 0x10);
                _ec.WriteIoPortByte(0x4E, 0x2F);
                _ec.WriteIoPortByte(0x4F, addr_lower);
                _ec.WriteIoPortByte(0x4E, 0x2E);
                _ec.WriteIoPortByte(0x4F, 0x12);
                _ec.WriteIoPortByte(0x4E, 0x2F);
                _ec.WriteIoPortByte(0x4F, 0x40);

                _hook = Hook.GlobalEvents();
                _hook.KeyDown += OnKeyDown;
                _hook.KeyUp += OnKeyUp;
            }
            catch (Exception ex)
            {
                Dispose(); // Clean up on failure
                throw;
            }
        }

        private void OnKeyDown(object? sender, KeyEventArgs e)
        {
            _pressed.Add(e.KeyCode);

            if (_pressed.Contains(Keys.LWin) &&
                _pressed.Contains(Keys.LMenu) &&
                _pressed.Contains(Keys.LControlKey))
            {
                TurboButtonPressed?.Invoke(this, EventArgs.Empty);
            }
        }

        private void OnKeyUp(object? sender, KeyEventArgs e)
        {
            _pressed.Remove(e.KeyCode);
        }

        public void Dispose()
        {
            if (_hook != null)
            {
                _hook.KeyDown -= OnKeyDown;
                _hook.KeyUp -= OnKeyUp;
                _hook.Dispose();
            }
            _ec?.Dispose();
        }
    }
}
