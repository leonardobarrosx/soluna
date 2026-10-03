using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Soluna.Client.UI;

/// <summary>Keyboard and mouse state for this frame and the last, for edge detection.</summary>
internal sealed class Input
{
    private KeyboardState _keys, _prevKeys;
    private MouseState _mouse, _prevMouse;

    public void Update(bool focused)
    {
        _prevKeys = _keys;
        _prevMouse = _mouse;
        _keys = focused ? Keyboard.GetState() : default;
        _mouse = focused ? Microsoft.Xna.Framework.Input.Mouse.GetState() : _prevMouse;
    }

    public bool Down(Keys key) => _keys.IsKeyDown(key);

    public bool Pressed(Keys key) => _keys.IsKeyDown(key) && !_prevKeys.IsKeyDown(key);

    public bool Ctrl => Down(Keys.LeftControl) || Down(Keys.RightControl);

    public Vector2 Mouse => _mouse.Position.ToVector2();

    public bool LeftDown => _mouse.LeftButton == ButtonState.Pressed;

    public bool RightDown => _mouse.RightButton == ButtonState.Pressed;

    public bool LeftPressed => LeftDown && _prevMouse.LeftButton == ButtonState.Released;

    /// <summary>Wheel notches since last frame, positive when scrolled up.</summary>
    public int Wheel => (_mouse.ScrollWheelValue - _prevMouse.ScrollWheelValue) / 120;
}
