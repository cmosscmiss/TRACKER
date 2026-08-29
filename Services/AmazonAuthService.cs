using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Tracker.Helpers;
using Tracker.Models;
using Windows.Foundation;

namespace Tracker.Services;

/// <summary>
/// Gestiona la sesión de Amazon en el navegador embebido: detecta si hay sesión iniciada (por la cookie de auth),
/// intenta un login BEST-EFFORT rellenando el formulario de Amazon con el usuario/contraseña dados (el usuario
/// completa a mano lo que no se pueda automatizar: captcha, verificación en dos pasos…) y cierra la sesión.
///
/// NO almacena credenciales: solo se usan para un autorrelleno puntual. La sesión persiste en el perfil de WebView2
/// (carpeta de datos de usuario compartida por el navegador visible y el pool de scraping), así que una vez iniciada
/// se mantiene entre arranques hasta que caduque o se cierre sesión.
/// </summary>
public sealed class AmazonAuthService
{
    #region Constants
    /// <summary>
    /// Prefijos del token de autenticación persistente de Amazon (presente con valor cuando hay sesión): <c>at-main</c>
    /// en amazon.com y <c>at-acbXX</c> en los marketplaces europeos (p. ej. <c>at-acbes</c>, <c>at-acbde</c>, <c>at-acbfr</c>…).
    /// </summary>
    private static bool IsAuthCookie(string name) =>
        string.Equals(name, "at-main", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("at-acb", StringComparison.OrdinalIgnoreCase);
    #endregion

    #region Attributes
    private readonly IOptions<AppSettings> _appSettings;
    private readonly ProgressService _progressService;

    /// <summary>Navegador VISIBLE (widget de búsqueda web): el que se usa para el login (para que el usuario vea captcha/2FA) y el logout.</summary>
    private WebView2? _loginBrowser;

    /// <summary>Navegadores adicionales (pool de scraping) para poder consultar cookies aunque el visible no esté cargado.</summary>
    private readonly List<WebView2> _cookieBrowsers = new();
    #endregion

    #region Events
    /// <summary>Se dispara cuando el estado de sesión PODRÍA haber cambiado (navegación del navegador visible), para refrescar la UI.</summary>
    public event EventHandler? StateChanged;

    /// <summary>Se dispara cuando el navegador VISIBLE queda registrado y listo (para la comprobación de sesión de arranque).</summary>
    public event EventHandler? LoginBrowserReady;
    #endregion

    #region Constructor
    public AmazonAuthService(IOptions<AppSettings> appSettings, ProgressService progressService)
    {
        _appSettings = appSettings;
        _progressService = progressService;
    }
    #endregion

    #region Methods (public)
    /// <summary>Registra el navegador VISIBLE (widget web), usado para login/logout. También sirve para consultar cookies.</summary>
    public void AttachLoginBrowser(WebView2 webView)
    {
        _loginBrowser = webView;
        AttachCookieBrowser(webView);
        LoginBrowserReady?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Registra un navegador (p. ej. del pool de scraping) para poder consultar cookies de sesión.</summary>
    public void AttachCookieBrowser(WebView2 webView)
    {
        if (!_cookieBrowsers.Contains(webView))
            _cookieBrowsers.Add(webView);
    }

    /// <summary>Notifica que el navegador visible ha navegado (el usuario pudo iniciar/cerrar sesión a mano); refresca la UI.</summary>
    public void NotifyNavigated() => StateChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>Host del marketplace de Amazon configurado en el navegador (es/de/…); por defecto amazon.es.</summary>
    private string Host => Amazon.HostForCountry(_appSettings.Value.WebViewControl.Country) ?? "www.amazon.es";

    /// <summary>Hay al menos un navegador listo para consultar cookies.</summary>
    public bool IsReady => FirstReadyCore() is not null;

    /// <summary>Hay un navegador VISIBLE listo para iniciar sesión (donde el usuario pueda ver/completar el login).</summary>
    public bool CanLogin => _loginBrowser?.CoreWebView2 is not null;

    /// <summary>True si hay sesión de Amazon iniciada en el marketplace configurado (existe la cookie de auth con valor).</summary>
    public Task<bool> IsLoggedInAsync() => IsLoggedInOnHostAsync(FirstReadyCore(), Host);

    /// <summary>Número total de marketplaces de Amazon soportados.</summary>
    public int StoreCount => Amazon.Marketplaces.Count;

    /// <summary>Cuenta en cuántos marketplaces soportados hay sesión iniciada (cookie de auth con valor).</summary>
    public async Task<int> CountLoggedInStoresAsync()
    {
        CoreWebView2? core = FirstReadyCore();
        if (core is null)
            return 0;

        int count = 0;
        foreach ((string _, string host, string _) in Amazon.Marketplaces)
            if (await IsLoggedInOnHostAsync(core, host))
                count++;
        return count;
    }

    /// <summary>
    /// Inicia sesión en Amazon con <paramref name="email"/>/<paramref name="password"/> en el navegador VISIBLE, y lo
    /// hace en TODOS los marketplaces soportados (la cuenta es la misma, pero la cookie de sesión es por dominio, así
    /// que hay que pasar por el acceso en cada tienda). Tras el primero, Amazon suele autenticar los demás sin pedir la
    /// contraseña (SSO central); aun así se rellena por si acaso. Es BEST-EFFORT: si Amazon pide captcha o verificación
    /// en dos pasos, se deja al usuario en el navegador. Devuelve true si al terminar hay sesión en el marketplace
    /// configurado. Debe llamarse en el hilo de UI.
    /// </summary>
    public async Task<bool> LoginAsync(string email, string password)
    {
        if (_loginBrowser?.CoreWebView2 is not CoreWebView2 core)
            return false;

        string emailJs = JsonSerializer.Serialize(email ?? string.Empty);
        string passwordJs = JsonSerializer.Serialize(password ?? string.Empty);

        // Marketplaces ordenados con el configurado primero (es el que decide el estado del botón).
        string primaryHost = Host;
        List<(string Host, string Label)> marketplaces = Amazon.Marketplaces
            .OrderByDescending(m => string.Equals(m.Host, primaryHost, StringComparison.OrdinalIgnoreCase))
            .Select(m => (m.Host, m.Label))
            .ToList();

        // Operación con barra del footer + entrada en la consola (progreso por tienda).
        ProgressNotifier operation = _progressService.StartOperation();
        int total = marketplaces.Count;
        int success = 0;
        try
        {
            for (int i = 0; i < total; i++)
            {
                (string host, string label) = marketplaces[i];
                operation.Message = string.Format(L(LocKeys.AmazonLogin_Progress_SigningIn), label, i + 1, total);
                operation.Progress = (int)(i * 100.0 / total);
                _progressService.ProgressNotifier.Report(operation);

                try { await LoginToHostAsync(core, host, emailJs, passwordJs); }
                catch { /* best-effort por tienda */ }

                // Tras enviar el formulario Amazon hace un redirect: la cookie de sesión tarda un poco en aparecer,
                // así que se sondea con reintentos (si no, se contaría como fallo aunque el login haya ido bien).
                if (await WaitForLoginOnHostAsync(core, host))
                    success++;

                await Task.Delay(300);
            }
        }
        finally
        {
            operation.Progress = 100;
            operation.Message = string.Format(L(LocKeys.AmazonLogin_Progress_Done), success, total);
            if (success == 0)
                operation.IsException = true;      // no se pudo iniciar sesión en ninguna
            else if (success < total)
                operation.IsWarning = true;        // parcial
            operation.FinishOperation();
            _progressService.ProgressNotifier.Report(operation);
            _progressService.FinishOperation();
        }

        bool loggedIn = await IsLoggedInAsync();
        NotifyNavigated();
        return loggedIn;
    }

    /// <summary>Cierra la sesión de Amazon en TODOS los marketplaces (la cookie de sesión es por dominio), con progreso.</summary>
    public async Task LogoutAsync()
    {
        if (_loginBrowser?.CoreWebView2 is not CoreWebView2 core)
            return;

        var marketplaces = Amazon.Marketplaces.ToList();
        int total = marketplaces.Count;

        ProgressNotifier operation = _progressService.StartOperation();
        try
        {
            for (int i = 0; i < total; i++)
            {
                var marketplace = marketplaces[i];
                operation.Message = string.Format(L(LocKeys.AmazonLogout_Progress_SigningOut), marketplace.Label, i + 1, total);
                operation.Progress = (int)(i * 100.0 / total);
                _progressService.ProgressNotifier.Report(operation);

                try { await NavigateAndWaitAsync(core, $"https://{marketplace.Host}/gp/flex/sign-out.html?action=sign-out"); }
                catch { /* best-effort por tienda */ }
            }
        }
        finally
        {
            operation.Progress = 100;
            operation.Message = L(LocKeys.AmazonLogout_Progress_Done);
            operation.FinishOperation();
            _progressService.ProgressNotifier.Report(operation);
            _progressService.FinishOperation();
        }

        NotifyNavigated();
    }
    #endregion

    #region Methods (private)
    /// <summary>Ejecuta el flujo de acceso (usuario → contraseña) en el marketplace <paramref name="host"/>. No hace nada si ya hay sesión ahí.</summary>
    private async Task LoginToHostAsync(CoreWebView2 core, string host, string emailJs, string passwordJs)
    {
        // Homepage del marketplace → enlace de acceso (con los parámetros openid correctos del país) → navegar a él.
        if (!await NavigateAndWaitAsync(core, $"https://{host}/"))
            return;
        await Task.Delay(600);
        if (await IsLoggedInOnHostAsync(core, host))
            return;   // ya con sesión en esta tienda (SSO de otra): nada que hacer

        string signInHref = await EvalStringAsync(core, @"(function(){
            var a=document.getElementById('nav-link-accountList');
            if(a&&a.href) return a.href;
            var l=document.querySelectorAll('a[href*=""/ap/signin""]');
            return l.length ? l[0].href : '';
        })();");
        if (string.IsNullOrWhiteSpace(signInHref))
            signInHref = $"https://{host}/ap/signin";

        if (!await NavigateAndWaitAsync(core, signInHref))
            return;

        // Paso 1 (USUARIO): rellena el email/teléfono (con eventos input+change para que Amazon habilite el botón) y
        // pulsa "Continuar". El botón es un <span id='continue'> con un <input> dentro: se pulsa el input interno (o
        // se envía el formulario) para que funcione de verdad, no el span. Si la página es combinada (email+contraseña
        // juntos) no habrá botón "Continuar" y se pasa directo al paso 2.
        await Task.Delay(600);
        await core.ExecuteScriptAsync($@"(function(){{
            var e=document.getElementById('ap_email')||document.getElementById('ap_email_login');
            if(e){{ e.value={emailJs};
                e.dispatchEvent(new Event('input',{{bubbles:true}}));
                e.dispatchEvent(new Event('change',{{bubbles:true}})); }}
        }})();");
        await Task.Delay(300);
        await ClickAndWaitAsync(core, @"(function(){
            var c=document.getElementById('continue');
            if(c){ var i=c.querySelector('input'); (i||c).click(); return 'yes'; }
            var i2=document.querySelector('input#continue');
            if(i2){ i2.click(); return 'yes'; }
            var e=document.getElementById('ap_email')||document.getElementById('ap_email_login');
            if(e&&e.form){ e.form.submit(); return 'yes'; }
            return 'no';
        })();");

        // Paso 2 (CONTRASEÑA): rellena la contraseña, marca "mantener sesión" si existe y pulsa "Iniciar sesión".
        await Task.Delay(600);
        await core.ExecuteScriptAsync($@"(function(){{
            var p=document.getElementById('ap_password');
            if(p){{ p.value={passwordJs};
                p.dispatchEvent(new Event('input',{{bubbles:true}}));
                p.dispatchEvent(new Event('change',{{bubbles:true}}));
                var rc=document.querySelector('input[name=rememberMe]'); if(rc&&!rc.checked) rc.click(); }}
        }})();");
        await Task.Delay(300);
        await ClickAndWaitAsync(core, @"(function(){
            var b=document.getElementById('signInSubmit');
            if(b){ var i=(b.querySelector&&b.querySelector('input')); (i||b).click(); return 'yes'; }
            var p=document.getElementById('ap_password');
            if(p&&p.form){ p.form.submit(); return 'yes'; }
            return 'no';
        })();");

        await Task.Delay(700);
    }

    /// <summary>Sondea la cookie de sesión del host con reintentos (para dar tiempo al redirect posterior al login).</summary>
    private static async Task<bool> WaitForLoginOnHostAsync(CoreWebView2 core, string host, int attempts = 6, int delayMs = 500)
    {
        for (int i = 0; i < attempts; i++)
        {
            if (await IsLoggedInOnHostAsync(core, host))
                return true;
            await Task.Delay(delayMs);
        }
        return false;
    }

    /// <summary>True si existe la cookie de auth (con valor) para el host dado.</summary>
    private static async Task<bool> IsLoggedInOnHostAsync(CoreWebView2? core, string host)
    {
        if (core is null)
            return false;

        try
        {
            IReadOnlyList<CoreWebView2Cookie> cookies = await core.CookieManager.GetCookiesAsync($"https://{host}");
            return cookies.Any(cookie => IsAuthCookie(cookie.Name) && !string.IsNullOrEmpty(cookie.Value));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Primer <see cref="CoreWebView2"/> listo (visible o del pool) para consultar cookies.</summary>
    private CoreWebView2? FirstReadyCore()
    {
        if (_loginBrowser?.CoreWebView2 is CoreWebView2 login)
            return login;

        foreach (WebView2 webView in _cookieBrowsers)
            if (webView.CoreWebView2 is CoreWebView2 core)
                return core;

        return null;
    }

    /// <summary>Navega a <paramref name="url"/> y espera a que termine la navegación (o falla tras el timeout).</summary>
    private static async Task<bool> NavigateAndWaitAsync(CoreWebView2 core, string url, int timeoutSeconds = 30)
    {
        var navigation = new TaskCompletionSource<bool>();
        TypedEventHandler<CoreWebView2, CoreWebView2NavigationCompletedEventArgs> handler = null!;
        handler = (_, args) =>
        {
            core.NavigationCompleted -= handler;
            navigation.TrySetResult(args.IsSuccess);
        };
        core.NavigationCompleted += handler;
        core.Navigate(url);

        Task finished = await Task.WhenAny(navigation.Task, Task.Delay(TimeSpan.FromSeconds(timeoutSeconds)));
        if (finished != navigation.Task)
        {
            core.NavigationCompleted -= handler;
            return false;
        }
        return navigation.Task.Result;
    }

    /// <summary>
    /// Ejecuta <paramref name="clickJs"/> (un IIFE que hace la pulsación y devuelve 'yes' si pulsó algo, 'no' si no) y,
    /// si pulsó algo y provoca navegación, espera a que termine. Si no había nada que pulsar (p. ej. página combinada
    /// sin botón "Continuar"), vuelve de inmediato. El handler se ata ANTES de ejecutar el click para no perder el evento.
    /// </summary>
    private static async Task ClickAndWaitAsync(CoreWebView2 core, string clickJs, int timeoutSeconds = 25)
    {
        var navigation = new TaskCompletionSource<bool>();
        TypedEventHandler<CoreWebView2, CoreWebView2NavigationCompletedEventArgs> handler = null!;
        handler = (_, args) =>
        {
            core.NavigationCompleted -= handler;
            navigation.TrySetResult(args.IsSuccess);
        };
        core.NavigationCompleted += handler;

        string clicked = await EvalStringAsync(core, clickJs);
        if (clicked != "yes")
        {
            core.NavigationCompleted -= handler;   // no había nada que pulsar: no esperamos navegación
            return;
        }

        await Task.WhenAny(navigation.Task, Task.Delay(TimeSpan.FromSeconds(timeoutSeconds)));
        core.NavigationCompleted -= handler;
    }

    /// <summary>Evalúa un script que devuelve un string y lo decodifica del JSON que retorna WebView2 (o vacío si falla).</summary>
    private static async Task<string> EvalStringAsync(CoreWebView2 core, string js)
    {
        try
        {
            return JsonSerializer.Deserialize<string>(await core.ExecuteScriptAsync(js)) ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>Texto localizado de una clave (o la propia clave si no hay servicio de localización).</summary>
    private static string L(string key) => LocalizationService.Instance?[key] ?? key;
    #endregion
}
