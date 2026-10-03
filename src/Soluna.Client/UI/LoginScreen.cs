using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Soluna.Client.Graphics;
using Soluna.Shared;

namespace Soluna.Client.UI;

internal enum LoginAction
{
    None,
    Login,
    Register,
}

/// <summary>Username and password, then log in or create the account.</summary>
internal sealed class LoginScreen
{
    private readonly TextField _user = new("Usuário", Constants.MaxUserLength);
    private readonly TextField _password = new("Senha", Constants.MaxPasswordLength, masked: true);
    private int _focus;
    private float _time;

    public LoginScreen(string user) => _user.Set(user);

    public string User => _user.Text.Trim();
    public string Password => _password.Text;

    /// <summary>Feedback under the form: an error from the server, or the connection state.</summary>
    public string Message { get; set; } = "";
    public bool MessageIsError { get; set; }

    /// <summary>False while there is no connection; the buttons wait for it.</summary>
    public bool Online { get; set; }

    /// <summary>Waiting for the server's answer.</summary>
    public bool Busy { get; set; }

    public void OnTextInput(char c) => (_focus == 0 ? _user : _password).OnTextInput(c);

    public LoginAction Update(Input input, Point screen, float dt)
    {
        _time += dt;
        if (input.Pressed(Keys.Tab) || input.Pressed(Keys.Down) || input.Pressed(Keys.Up)) _focus = 1 - _focus;

        var layout = Layout(screen);
        var mouse = input.Mouse.ToPoint();
        if (input.LeftPressed)
        {
            if (UserRect(layout).Contains(mouse)) _focus = 0;
            if (PasswordRect(layout).Contains(mouse)) _focus = 1;
            if (LoginButton(layout).Contains(mouse)) return Submit(LoginAction.Login);
            if (RegisterButton(layout).Contains(mouse)) return Submit(LoginAction.Register);
        }

        if (input.Pressed(Keys.Enter))
        {
            if (_focus == 0 && Password.Length == 0) _focus = 1;
            else return Submit(LoginAction.Login);
        }
        return LoginAction.None;
    }

    private LoginAction Submit(LoginAction action)
    {
        if (!Online || Busy) return LoginAction.None;
        if (User.Length == 0 || Password.Length == 0)
        {
            Message = "Preencha usuário e senha.";
            MessageIsError = true;
            return LoginAction.None;
        }
        Busy = true;
        Message = action == LoginAction.Register ? "Criando conta..." : "Entrando...";
        MessageIsError = false;
        return action;
    }

    public void Draw(SpriteBatch batch, Texture2D pixel, Fonts fonts, Point screen)
    {
        var layout = Layout(screen);
        Ui.Panel(batch, pixel, layout);

        var title = fonts.Title.MeasureString(Constants.GameName);
        Ui.Text(batch, fonts.Title, Constants.GameName, new Vector2(layout.Center.X - title.X / 2, layout.Y + 24), Theme.Luna);
        const string subtitle = "Entre na sua conta";
        var sub = fonts.Small.MeasureString(subtitle);
        Ui.Text(batch, fonts.Small, subtitle, new Vector2(layout.Center.X - sub.X / 2, layout.Y + 56), Theme.TextDim);

        _user.Draw(batch, pixel, fonts, UserRect(layout), _focus == 0, _time);
        _password.Draw(batch, pixel, fonts, PasswordRect(layout), _focus == 1, _time);

        var ready = Online && !Busy;
        Button(batch, pixel, fonts, LoginButton(layout), "Entrar", ready, primary: true);
        Button(batch, pixel, fonts, RegisterButton(layout), "Criar conta", ready, primary: false);

        var message = Online ? Message : "Conectando ao servidor...";
        if (message.Length > 0)
        {
            var size = fonts.Small.MeasureString(message);
            var color = Online && MessageIsError ? Theme.Danger : Theme.TextDim;
            Ui.Text(batch, fonts.Small, message, new Vector2(layout.Center.X - size.X / 2, layout.Bottom - 30), color);
        }
    }

    private static void Button(SpriteBatch batch, Texture2D pixel, Fonts fonts, Rectangle rect, string text, bool ready, bool primary)
    {
        batch.Draw(pixel, rect, ready && primary ? Theme.Luna * 0.25f : Theme.PanelRaised);
        Ui.Outline(batch, pixel, rect, ready && primary ? Theme.Luna : Theme.Border);
        var size = fonts.Body.MeasureString(text);
        Ui.Text(batch, fonts.Body, text, new Vector2(rect.Center.X - size.X / 2, rect.Center.Y - size.Y / 2), ready ? Theme.Text : Theme.TextDim);
    }

    private static Rectangle Layout(Point screen)
    {
        const int w = 400, h = 340;
        return new Rectangle((screen.X - w) / 2, (screen.Y - h) / 2 - 20, w, h);
    }

    private static Rectangle UserRect(Rectangle l) => new(l.X + 32, l.Y + 90, l.Width - 64, 46);
    private static Rectangle PasswordRect(Rectangle l) => new(l.X + 32, l.Y + 146, l.Width - 64, 46);
    private static Rectangle LoginButton(Rectangle l) => new(l.X + 32, l.Y + 214, l.Width - 64, 38);
    private static Rectangle RegisterButton(Rectangle l) => new(l.X + 32, l.Y + 260, l.Width - 64, 34);
}
