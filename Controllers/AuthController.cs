using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc;
using System.Net.Http;
using System.Security.Claims;
using System.Text.Json;

namespace DV.Admin.UI.Controllers
{
    public class AuthController : Controller
    {
        private readonly IConfiguration _configuration;
        private readonly IHttpClientFactory _httpClientFactory;

        public AuthController(IConfiguration configuration, IHttpClientFactory httpClientFactory)
        {
            _configuration = configuration;
            _httpClientFactory = httpClientFactory;
        }

        /// <summary>
        /// Login choice page - shows MVC view with login options
        /// </summary>
        [HttpGet("/auth/login")]
        [AllowAnonymous]
        public IActionResult Login(string? returnUrl = "/")
        {
            // If already authenticated, go to home
            if (User.Identity?.IsAuthenticated == true)
            {
                return Redirect(returnUrl ?? "/");
            }
            
            // Show login choice view
            ViewData["ReturnUrl"] = returnUrl;
            ViewData["AdfsHost"] = GetAdfsHost();
            return View("LoginChoice");
        }

        /// <summary>
        /// Alternate route for login choice page
        /// </summary>
        [HttpGet("/login-choice")]
        [AllowAnonymous]
        public IActionResult LoginChoice(string? returnUrl = "/")
        {
            // If already authenticated, go to home
            if (User.Identity?.IsAuthenticated == true)
            {
                return Redirect(returnUrl ?? "/");
            }
            
            ViewData["ReturnUrl"] = returnUrl;
            ViewData["AdfsHost"] = GetAdfsHost();
            return View("LoginChoice");
        }

        private string GetAdfsHost()
        {
            var authority = _configuration["Adfs:Authority"] ?? "AD FS Server";
            try
            {
                var uri = new Uri(authority);
                return uri.Host;
            }
            catch
            {
                return authority;
            }
        }

        /// <summary>
        /// SSO Login - Uses Windows Integrated Authentication via AD FS
        /// This triggers OIDC challenge with wauth parameter for WIA
        /// </summary>
        [HttpGet("/auth/login-sso")]
        [AllowAnonymous]
        public IActionResult LoginSso(string? returnUrl = "/")
        {
            var properties = new AuthenticationProperties
            {
                RedirectUri = returnUrl ?? "/",
                Items = { { "LoginMethod", "SSO" } }
            };
            
            return Challenge(properties, OpenIdConnectDefaults.AuthenticationScheme);
        }

        /// <summary>
        /// Password Login - Uses Resource Owner Password Credentials (ROPC) flow with AD FS
        /// This allows logging in as a different user for debugging purposes
        /// </summary>
        [HttpGet("/auth/login-password")]
        [AllowAnonymous]
        public IActionResult LoginPasswordGet(string? username = null, string? returnUrl = "/")
        {
            // Show the password entry form
            ViewData["Username"] = username;
            ViewData["ReturnUrl"] = returnUrl;
            return View("PasswordLogin");
        }

        /// <summary>
        /// Password Login POST - Authenticates with AD FS using ROPC flow
        /// </summary>
        [HttpPost("/auth/login-password")]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> LoginPasswordPost(string username, string password, string? returnUrl = "/")
        {
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                ViewData["Error"] = "Username and password are required.";
                ViewData["Username"] = username;
                ViewData["ReturnUrl"] = returnUrl;
                return View("PasswordLogin");
            }

            try
            {
                // Get AD FS token endpoint
                var authority = _configuration["Adfs:Authority"] ?? throw new InvalidOperationException("AD FS Authority not configured");
                var clientId = _configuration["Adfs:ClientId"] ?? throw new InvalidOperationException("AD FS ClientId not configured");
                var clientSecret = _configuration["Adfs:ClientSecret"];
                
                // AD FS token endpoint
                var tokenEndpoint = $"{authority.TrimEnd('/')}/oauth2/token";

                // Normalize username (support both AD\user and user@domain formats)
                var normalizedUsername = username;
                if (!username.Contains("\\") && !username.Contains("@"))
                {
                    // Assume domain prefix if not specified
                    normalizedUsername = $"AD\\{username}";
                }

                // Build ROPC request
                var tokenRequest = new Dictionary<string, string>
                {
                    { "grant_type", "password" },
                    { "client_id", clientId },
                    { "username", normalizedUsername },
                    { "password", password },
                    { "scope", "openid profile" }
                };

                if (!string.IsNullOrEmpty(clientSecret))
                {
                    tokenRequest["client_secret"] = clientSecret;
                }

                var client = _httpClientFactory.CreateClient();
                var response = await client.PostAsync(tokenEndpoint, new FormUrlEncodedContent(tokenRequest));

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"ROPC auth failed: {response.StatusCode} - {errorContent}");
                    
                    ViewData["Error"] = "Invalid username or password. Please try again.";
                    ViewData["Username"] = username;
                    ViewData["ReturnUrl"] = returnUrl;
                    return View("PasswordLogin");
                }

                var tokenResponse = await response.Content.ReadAsStringAsync();
                var tokenData = JsonSerializer.Deserialize<JsonElement>(tokenResponse);

                // Extract claims from ID token or access token
                var accessToken = tokenData.GetProperty("access_token").GetString();
                var idToken = tokenData.TryGetProperty("id_token", out var idTokenElement) 
                    ? idTokenElement.GetString() 
                    : null;

                // Parse the token to get claims (simplified - in production use proper JWT validation)
                var claims = new List<Claim>
                {
                    new Claim("unique_name", normalizedUsername),
                    new Claim(ClaimTypes.Name, normalizedUsername),
                    new Claim("auth_method", "password")
                };

                // If we have an ID token, parse additional claims
                if (!string.IsNullOrEmpty(idToken))
                {
                    var tokenParts = idToken.Split('.');
                    if (tokenParts.Length >= 2)
                    {
                        try
                        {
                            var payload = tokenParts[1];
                            // Add padding if needed
                            var paddedPayload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
                            var payloadBytes = Convert.FromBase64String(paddedPayload.Replace('-', '+').Replace('_', '/'));
                            var payloadJson = System.Text.Encoding.UTF8.GetString(payloadBytes);
                            var payloadData = JsonSerializer.Deserialize<JsonElement>(payloadJson);

                            if (payloadData.TryGetProperty("upn", out var upn))
                                claims.Add(new Claim("upn", upn.GetString() ?? ""));
                            if (payloadData.TryGetProperty("unique_name", out var uniqueName))
                            {
                                // Update the unique_name claim with the actual value from token
                                claims.RemoveAll(c => c.Type == "unique_name");
                                claims.Add(new Claim("unique_name", uniqueName.GetString() ?? normalizedUsername));
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Failed to parse ID token: {ex.Message}");
                        }
                    }
                }

                // Create identity and sign in
                var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme, "unique_name", "role");
                var principal = new ClaimsPrincipal(identity);

                await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, new AuthenticationProperties
                {
                    IsPersistent = false,
                    ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8)
                });

                Console.WriteLine($"=== Password Login Successful ===");
                Console.WriteLine($"User: {normalizedUsername}");

                return Redirect(returnUrl ?? "/");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Password login error: {ex.Message}");
                ViewData["Error"] = $"Authentication failed: {ex.Message}";
                ViewData["Username"] = username;
                ViewData["ReturnUrl"] = returnUrl;
                return View("PasswordLogin");
            }
        }

        [HttpGet("/logout")]
        [HttpPost("/logout")]
        public IActionResult Logout()
        {
            return SignOut(new AuthenticationProperties
            {
                RedirectUri = "/login-choice"
            }, CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme);
        }

        /// <summary>
        /// Debug endpoint to see all claims from the current user
        /// </summary>
        [HttpGet("/auth/claims")]
        public IActionResult Claims()
        {
            var claims = User.Claims.Select(c => new { c.Type, c.Value }).ToList();
            return Ok(new
            {
                IsAuthenticated = User.Identity?.IsAuthenticated,
                Name = User.Identity?.Name,
                AuthType = User.Identity?.AuthenticationType,
                ClaimsCount = claims.Count,
                Claims = claims
            });
        }
    }
}
