using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Security.Cryptography;
using LuminaMoney.Api.Data;
using Microsoft.IdentityModel.Tokens;
namespace LuminaMoney.Api.Security;
public sealed record IssuedTokens(string AccessToken,DateTime AccessExpiresUtc,string RefreshToken,string RefreshTokenHash,DateTime RefreshExpiresUtc);
public sealed class TokenService(IConfiguration config)
{
 public IssuedTokens Create(AppUser user)
 {
  var expires=DateTime.UtcNow.AddMinutes(config.GetValue("Jwt:AccessTokenMinutes",30));
  var claims=new[]{new Claim("uid",user.Id.ToString()),new Claim(JwtRegisteredClaimNames.Email,user.Email!),new Claim("name",user.DisplayName),new Claim(JwtRegisteredClaimNames.Jti,Guid.NewGuid().ToString())};
  var credentials=new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Key"]!)),SecurityAlgorithms.HmacSha256);
  var token=new JwtSecurityToken(config["Jwt:Issuer"],config["Jwt:Audience"],claims,expires:expires,signingCredentials:credentials);
  var refresh=Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));var refreshExpiry=DateTime.UtcNow.AddDays(config.GetValue("Jwt:RefreshTokenDays",30));
  return new(new JwtSecurityTokenHandler().WriteToken(token),expires,refresh,Hash(refresh),refreshExpiry);
 }
 public static string Hash(string token)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
