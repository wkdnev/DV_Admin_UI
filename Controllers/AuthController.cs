using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc;
using System.Net.Http;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using DV.Shared.Interfaces;
using DV.Shared.Security;
using DV.Admin.UI.Services;

namespace DV.Admin.UI.Controllers
{
    public class AuthController : Controller
    {
        private readonly IConfiguration _configuration;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ICredentialService _credentialService;
        private readonly AuditService _auditService;
        private readonly ISessionManagementService _sessionService;

        public AuthController(IConfiguration configuration, IHttpClientFactory httpClientFactory, ICredentialService credentialService, AuditService auditService, ISessionManagementService sessionService)
        {
            _configuration = configuration;
            _httpClientFactory = httpClientFactory;
            _credentialService = credentialService;
            _auditService = auditService;
            _sessionService = sessionService;
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

            var authMode = _configuration["Auth:Mode"] ?? "ADFS";
            var isInternalAuth = authMode.Equals("Internal", StringComparison.OrdinalIgnoreCase);

            // In Internal mode, skip login choice and go straight to password login
            if (isInternalAuth)
            {
                return Redirect($"/auth/login-password?returnUrl={Uri.EscapeDataString(returnUrl ?? "/")}");
            }
            
            // Show login choice view
            ViewData["ReturnUrl"] = returnUrl;
            ViewData["AdfsHost"] = GetAdfsHost();
            ViewData["AuthMode"] = authMode;
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

            var authMode = _configuration["Auth:Mode"] ?? "ADFS";
            var isInternalAuth = authMode.Equals("Internal", StringComparison.OrdinalIgnoreCase);

            if (isInternalAuth)
            {
                return Redirect($"/auth/login-password?returnUrl={Uri.EscapeDataString(returnUrl ?? "/")}");
            }
            
            ViewData["ReturnUrl"] = returnUrl;
            ViewData["AdfsHost"] = GetAdfsHost();
            ViewData["AuthMode"] = authMode;
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
            var authMode = _configuration["Auth:Mode"] ?? "ADFS";
            if (authMode.Equals("Internal", StringComparison.OrdinalIgnoreCase))
            {
                return Redirect($"/auth/login-password?returnUrl={Uri.EscapeDataString(returnUrl ?? "/")}");
            }

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
            ViewData["AuthMode"] = _configuration["Auth:Mode"] ?? "ADFS";
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
                ViewData["AuthMode"] = _configuration["Auth:Mode"] ?? "ADFS";
                return View("PasswordLogin");
            }

            // ── Try local credential authentication first (NIST IA-5) ──
            try
            {
                var localUser = await _credentialService.ValidateCredentialAsync(username, password);
                if (localUser != null)
                {
                    // Check MustChangePassword (NIST IA-5(1)(f))
                    var credential = await _credentialService.GetCredentialInfoAsync(localUser.UserId);
                    if (credential?.MustChangePassword == true)
                    {
                        TempData["MustChangePasswordUserId"] = localUser.UserId;
                        TempData["MustChangePasswordUsername"] = localUser.Username;
                        TempData["ReturnUrl"] = returnUrl;
                        return Redirect("/auth/change-password");
                    }

                    var claims = new List<Claim>
                    {
                        new Claim("unique_name", localUser.Username),
                        new Claim(ClaimTypes.Name, localUser.Username),
                        new Claim("auth_method", "local"),
                        new Claim("user_id", localUser.UserId.ToString())
                    };

                    if (!string.IsNullOrEmpty(localUser.Email))
                        claims.Add(new Claim(ClaimTypes.Email, localUser.Email));
                    if (!string.IsNullOrEmpty(localUser.DisplayName))
                        claims.Add(new Claim("display_name", localUser.DisplayName));

                    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme, "unique_name", "role");
                    var principal = new ClaimsPrincipal(identity);

                    var authProps = new AuthenticationProperties
                    {
                        IsPersistent = false,
                        ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8)
                    };

                    // In Internal mode, acquire API JWT for subsequent API calls
                    var loginAuthMode = _configuration["Auth:Mode"] ?? "ADFS";
                    if (loginAuthMode.Equals("Internal", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            var apiBaseUrl = _configuration["Api:BaseUrl"] ?? "https://localhost:5002";
                            var client = _httpClientFactory.CreateClient();
                            var tokenRequestBody = new StringContent(
                                JsonSerializer.Serialize(new { username, password }),
                                Encoding.UTF8, "application/json");
                            var tokenResponse = await client.PostAsync($"{apiBaseUrl}/api/auth/token", tokenRequestBody);
                            if (tokenResponse.IsSuccessStatusCode)
                            {
                                var tokenData = JsonSerializer.Deserialize<JsonElement>(await tokenResponse.Content.ReadAsStringAsync());
                                var apiToken = tokenData.GetProperty("access_token").GetString();
                                if (!string.IsNullOrEmpty(apiToken))
                                {
                                    authProps.StoreTokens(new[] { new AuthenticationToken { Name = "access_token", Value = apiToken } });
                                }
                            }
                        }
                        catch (Exception tokenEx)
                        {
                            Console.WriteLine($"Warning: Could not acquire API token: {tokenEx.Message}");
                        }
                    }

                    await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, authProps);

                    // NIST AU-2: Log successful login
                    await _auditService.LogAuthenticationAsync(localUser.Username, AuditActions.Login, AuditResults.Success, "Local credential login via DV.Admin.UI");

                    Console.WriteLine($"=== Local Login Successful ===");
                    Console.WriteLine($"User: {localUser.Username} (ID: {localUser.UserId})");

                    return Redirect(returnUrl ?? "/");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Local credential check failed: {ex.Message}");
                // Fall through to ROPC (if ADFS mode)
            }

            // ── In Internal mode, local auth is the only option ──
            var authMode = _configuration["Auth:Mode"] ?? "ADFS";
            if (authMode.Equals("Internal", StringComparison.OrdinalIgnoreCase))
            {
                // NIST AU-2: Log failed login attempt
                await _auditService.LogAuthenticationAsync(username, AuditActions.LoginFailed, AuditResults.Failed, "Invalid credentials in Internal auth mode via DV.Admin.UI");
                ViewData["Error"] = "Invalid username or password. Please try again.";
                ViewData["Username"] = username;
                ViewData["ReturnUrl"] = returnUrl;
                ViewData["AuthMode"] = _configuration["Auth:Mode"] ?? "ADFS";
                return View("PasswordLogin");
            }

            // ── Fall through to AD FS ROPC for domain users ──
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
                    ViewData["AuthMode"] = _configuration["Auth:Mode"] ?? "ADFS";
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

                            // Extract group claims and map them to role claims
                            if (payloadData.TryGetProperty("groups", out var groups))
                            {
                                if (groups.ValueKind == JsonValueKind.Array)
                                {
                                    foreach (var group in groups.EnumerateArray())
                                    {
                                        var groupName = group.GetString();
                                        if (!string.IsNullOrEmpty(groupName))
                                        {
                                            claims.Add(new Claim("groups", groupName));
                                            claims.Add(new Claim("role", groupName));
                                            Console.WriteLine($"  Added group/role claim: {groupName}");
                                        }
                                    }
                                }
                                else if (groups.ValueKind == JsonValueKind.String)
                                {
                                    var groupName = groups.GetString();
                                    if (!string.IsNullOrEmpty(groupName))
                                    {
                                        claims.Add(new Claim("groups", groupName));
                                        claims.Add(new Claim("role", groupName));
                                        Console.WriteLine($"  Added group/role claim: {groupName}");
                                    }
                                }
                            }

                            // Also extract role claims directly if present
                            if (payloadData.TryGetProperty("role", out var roles))
                            {
                                if (roles.ValueKind == JsonValueKind.Array)
                                {
                                    foreach (var role in roles.EnumerateArray())
                                    {
                                        var roleName = role.GetString();
                                        if (!string.IsNullOrEmpty(roleName) && !claims.Any(c => c.Type == "role" && c.Value == roleName))
                                        {
                                            claims.Add(new Claim("role", roleName));
                                            Console.WriteLine($"  Added role claim: {roleName}");
                                        }
                                    }
                                }
                                else if (roles.ValueKind == JsonValueKind.String)
                                {
                                    var roleName = roles.GetString();
                                    if (!string.IsNullOrEmpty(roleName) && !claims.Any(c => c.Type == "role" && c.Value == roleName))
                                    {
                                        claims.Add(new Claim("role", roleName));
                                        Console.WriteLine($"  Added role claim: {roleName}");
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Failed to parse ID token: {ex.Message}");
                        }
                    }
                }

                // Also parse the access token for group/role claims (AD FS often puts groups in access token)
                if (!string.IsNullOrEmpty(accessToken) && !claims.Any(c => c.Type == "role"))
                {
                    var tokenParts = accessToken.Split('.');
                    if (tokenParts.Length >= 2)
                    {
                        try
                        {
                            var payload = tokenParts[1];
                            var paddedPayload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
                            var payloadBytes = Convert.FromBase64String(paddedPayload.Replace('-', '+').Replace('_', '/'));
                            var payloadJson = System.Text.Encoding.UTF8.GetString(payloadBytes);
                            var payloadData = JsonSerializer.Deserialize<JsonElement>(payloadJson);

                            Console.WriteLine($"Access token claims:");
                            foreach (var prop in payloadData.EnumerateObject())
                            {
                                Console.WriteLine($"  AT: {prop.Name} = {prop.Value}");
                            }

                            // Extract groups from access token
                            if (payloadData.TryGetProperty("groups", out var atGroups))
                            {
                                if (atGroups.ValueKind == JsonValueKind.Array)
                                {
                                    foreach (var group in atGroups.EnumerateArray())
                                    {
                                        var groupName = group.GetString();
                                        if (!string.IsNullOrEmpty(groupName) && !claims.Any(c => c.Type == "role" && c.Value == groupName))
                                        {
                                            claims.Add(new Claim("groups", groupName));
                                            claims.Add(new Claim("role", groupName));
                                            Console.WriteLine($"  AT: Added group/role claim: {groupName}");
                                        }
                                    }
                                }
                            }

                            // Extract role from access token
                            if (payloadData.TryGetProperty("role", out var atRoles))
                            {
                                if (atRoles.ValueKind == JsonValueKind.Array)
                                {
                                    foreach (var role in atRoles.EnumerateArray())
                                    {
                                        var roleName = role.GetString();
                                        if (!string.IsNullOrEmpty(roleName) && !claims.Any(c => c.Type == "role" && c.Value == roleName))
                                        {
                                            claims.Add(new Claim("role", roleName));
                                            Console.WriteLine($"  AT: Added role claim: {roleName}");
                                        }
                                    }
                                }
                                else if (atRoles.ValueKind == JsonValueKind.String)
                                {
                                    var roleName = atRoles.GetString();
                                    if (!string.IsNullOrEmpty(roleName) && !claims.Any(c => c.Type == "role" && c.Value == roleName))
                                    {
                                        claims.Add(new Claim("role", roleName));
                                        Console.WriteLine($"  AT: Added role claim: {roleName}");
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Failed to parse access token: {ex.Message}");
                        }
                    }
                }

                // Create identity and sign in
                var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme, "unique_name", "role");
                var principal = new ClaimsPrincipal(identity);

                Console.WriteLine($"=== Password Login Successful ===");
                Console.WriteLine($"User: {normalizedUsername}");
                Console.WriteLine($"Total claims: {claims.Count}");
                foreach (var c in claims)
                {
                    Console.WriteLine($"  Final claim: {c.Type} = {c.Value}");
                }

                await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, new AuthenticationProperties
                {
                    IsPersistent = false,
                    ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8)
                });

                return Redirect(returnUrl ?? "/");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Password login error: {ex.Message}");
                ViewData["Error"] = $"Authentication failed: {ex.Message}";
                ViewData["Username"] = username;
                ViewData["ReturnUrl"] = returnUrl;
                ViewData["AuthMode"] = _configuration["Auth:Mode"] ?? "ADFS";
                return View("PasswordLogin");
            }
        }

        /// <summary>
        /// Change Password GET - Shows form for mandatory password change (NIST IA-5(1)(f))
        /// </summary>
        [HttpGet("/auth/change-password")]
        [AllowAnonymous]
        public IActionResult ChangePasswordGet()
        {
            var userId = TempData["MustChangePasswordUserId"];
            var username = TempData["MustChangePasswordUsername"] as string;

            if (userId == null || string.IsNullOrEmpty(username))
            {
                return Redirect("/auth/login");
            }

            // Preserve TempData for the POST
            TempData.Keep("MustChangePasswordUserId");
            TempData.Keep("MustChangePasswordUsername");
            TempData.Keep("ReturnUrl");

            ViewData["Username"] = username;
            return View("ChangePassword");
        }

        /// <summary>
        /// Change Password POST - Processes mandatory password change
        /// </summary>
        [HttpPost("/auth/change-password")]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePasswordPost(string currentPassword, string newPassword, string confirmPassword)
        {
            var userId = TempData["MustChangePasswordUserId"] as int?;
            var username = TempData["MustChangePasswordUsername"] as string;
            var returnUrl = TempData["ReturnUrl"] as string ?? "/";

            if (userId == null || string.IsNullOrEmpty(username))
            {
                return Redirect("/auth/login");
            }

            if (string.IsNullOrEmpty(currentPassword) || string.IsNullOrEmpty(newPassword))
            {
                TempData["MustChangePasswordUserId"] = userId;
                TempData["MustChangePasswordUsername"] = username;
                TempData["ReturnUrl"] = returnUrl;
                ViewData["Error"] = "All fields are required.";
                ViewData["Username"] = username;
                return View("ChangePassword");
            }

            if (newPassword != confirmPassword)
            {
                TempData["MustChangePasswordUserId"] = userId;
                TempData["MustChangePasswordUsername"] = username;
                TempData["ReturnUrl"] = returnUrl;
                ViewData["Error"] = "New passwords do not match.";
                ViewData["Username"] = username;
                return View("ChangePassword");
            }

            if (currentPassword == newPassword)
            {
                TempData["MustChangePasswordUserId"] = userId;
                TempData["MustChangePasswordUsername"] = username;
                TempData["ReturnUrl"] = returnUrl;
                ViewData["Error"] = "New password must be different from current password.";
                ViewData["Username"] = username;
                return View("ChangePassword");
            }

            var changeError = await _credentialService.ChangePasswordAsync(userId.Value, currentPassword, newPassword);
            if (changeError != null)
            {
                TempData["MustChangePasswordUserId"] = userId;
                TempData["MustChangePasswordUsername"] = username;
                TempData["ReturnUrl"] = returnUrl;
                ViewData["Error"] = changeError;
                ViewData["Username"] = username;
                return View("ChangePassword");
            }

            // Sign them in now
            var claims = new List<Claim>
            {
                new Claim("unique_name", username),
                new Claim(ClaimTypes.Name, username),
                new Claim("auth_method", "local"),
                new Claim("user_id", userId.Value.ToString())
            };

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme, "unique_name", "role");
            var principal = new ClaimsPrincipal(identity);

            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, new AuthenticationProperties
            {
                IsPersistent = false,
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8)
            });

            return Redirect(returnUrl);
        }

        /// <summary>
        /// Logout — clears the authentication cookie and redirects to login choice.
        /// Does NOT trigger OIDC sign-out (which can cause AD FS WIA to auto-re-authenticate).
        /// </summary>
        [HttpGet("/logout")]
        [HttpPost("/logout")]
        public async Task<IActionResult> Logout()
        {
            var username = User.Identity?.Name ?? "Unknown";

            // NIST SC-23(01): Terminate the server-side session record in DB
            try
            {
                var sessionKey = HttpContext.Session?.Id;
                if (!string.IsNullOrEmpty(sessionKey))
                {
                    await _sessionService.TerminateSessionAsync(sessionKey);
                }
            }
            catch (Exception ex)
            {
                // Log but don't block logout
                HttpContext.RequestServices.GetService<ILogger<AuthController>>()?.
                    LogWarning(ex, "Failed to terminate DB session during logout for {Username}", username);
            }

            // NIST SC-23(01): Clear and abandon the ASP.NET session
            HttpContext.Session?.Clear();

            // Sign out the authentication cookie
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

            // NIST AU-2: Log logout event
            await _auditService.LogAuthenticationAsync(username, AuditActions.Logout, AuditResults.Success, "User logged out");

            // NIST AC-12(02): Redirect to explicit logout confirmation page
            return Redirect("/auth/logged-out");
        }

        /// <summary>
        /// NIST AC-12(02): Explicit logout confirmation page.
        /// Displays a clear message that the session has ended.
        /// </summary>
        [HttpGet("/auth/logged-out")]
        [AllowAnonymous]
        public IActionResult LoggedOut()
        {
            return View("LoggedOut");
        }

        /// <summary>
        /// NIST AC-12(03): Session expired page.
        /// Shown when a session times out due to inactivity or absolute timeout.
        /// </summary>
        [HttpGet("/auth/session-expired")]
        [AllowAnonymous]
        public IActionResult SessionExpired()
        {
            return View("SessionExpired");
        }

        /// <summary>
        /// Full SSO logout — signs out of both cookie and OIDC (AD FS).
        /// Use when the user explicitly wants to end their AD FS session too.
        /// </summary>
        [HttpGet("/logout-sso")]
        [HttpPost("/logout-sso")]
        public async Task<IActionResult> LogoutSso()
        {
            var authMode = _configuration["Auth:Mode"] ?? "ADFS";
            if (authMode.Equals("Internal", StringComparison.OrdinalIgnoreCase))
            {
                await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return Redirect("/auth/login");
            }

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
