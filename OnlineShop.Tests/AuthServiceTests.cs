using Xunit;
using OnlineShop.Services; // Теперь это будет работать после добавления Reference
using OnlineShop.Models;

namespace OnlineShop.Tests
{
    public class AuthServiceTests
    {
        private readonly AuthService _authService;

        public AuthServiceTests()
        {
            // Создаем новый экземпляр перед каждым тестом
            _authService = new AuthService();
        }

        [Fact]
        public void Register_ValidUser_ReturnsSuccess()
        {
            // Arrange
            var email = "newuser@example.com";
            var password = "Password123";

            // Act
            var result = _authService.Register(email, password);

            // Assert
            Assert.Contains("Success", result);
        }

        [Theory]
        [InlineData("short", "at least 8 characters")]
        [InlineData("nodigits", "at least one digit")]
        [InlineData("NONUMBER", "lowercase letter")]
        public void Register_InvalidPassword_ReturnsSpecificError(string pass, string errorSnippet)
        {
            // Act
            var result = _authService.Register("test@test.com", pass);

            // Assert
            Assert.Contains(errorSnippet, result);
        }
    }
}