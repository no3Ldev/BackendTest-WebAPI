using BackendTestWebAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace BackendTestWebAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class LoginController : BaseController
    {
        private readonly AppDbContext _context;

        public LoginController(IConfiguration config, AppDbContext context) : base(config)
        {
            _context = context;
        }

        [HttpPost]
        public async Task<ActionResult<LoginResponse>> Post(LoginParam param)
        {
            try
            {
                if (param.Command != "login")
                    return BadRequest();

                var response = new LoginResponse
                {
                    Command = param.Command,
                    UsernameOrEmail = param.UsernameOrEmail,
                    Challenge = param.Challenge,
                    Success = false
                };

                var actualUsername = _context.Users //find user on username and email
                    .Where(u => u.Username == param.UsernameOrEmail || u.Email == param.UsernameOrEmail)
                    .Select(u => u.Username)
                    .FirstOrDefault();

                if (string.IsNullOrEmpty(actualUsername))
                {
                    response.Remarks = "Invalid username or email";
                    return response;
                }

                var auth = _context.Authentications
                    .OrderByDescending(a => a.Id)
                    .Where(a => a.Username == actualUsername && a.ExpirationTime >= DateTime.Now)
                    .FirstOrDefault();

                if (auth != null) //check if salt is still valid
                {
                    var user = _context.Users.FirstOrDefault(u => u.Username == param.UsernameOrEmail);

                    var ctrlHash = new HashController();
                    var expectedChallenge = ctrlHash.Get(user.Password, auth.Salt);

                    if (param.Challenge == expectedChallenge) //check if password/challenge is valid
                    {
                        var sessionId = ctrlHash.Get(param.UsernameOrEmail, DateTime.Now.ToString()); //TODO - get actual session id
                        var sessionExpiry = int.Parse(GetSettings("session_expiry")); //in seconds

                        var login = new Login()
                        {
                            Username = actualUsername,
                            SessionId = sessionId,
                            Timestamp = DateTime.Now,
                            SessionExpiry = DateTime.Now.AddSeconds(sessionExpiry)
                        };

                        _context.Logins.Add(login);

                        await _context.SaveChangesAsync().ConfigureAwait(false);

                        response.Success = true;
                        response.SessionId = sessionId;
                        response.UserId = user.Id;
                        response.Validity = auth.Validity;

                        return response;
                    }
                    else
                    {
                        response.Remarks = "Login challenge invalid or wrong password";
                        return response;
                    }
                }
                else
                {
                    response.Remarks = "Login salt not found or no longer valid";
                    return response;
                }
            }
            catch (Exception ex)
            {
                return ErrorCode(ex.Message);
            }
        }
    }
}
