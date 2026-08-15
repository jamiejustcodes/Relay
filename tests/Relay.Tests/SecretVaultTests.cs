using FluentAssertions;
using Relay.Infrastructure.Security;
using Xunit;

namespace Relay.Tests;

public class SecretVaultTests
{
    [Fact]
    public void DpapiSecretVault_RoundtripEncryption_ShouldRestoreOriginalPlainText()
    {
        var vault = new DpapiSecretVault();
        string originalSecret = "AIzaSy_Relay_TestKey_123456789";

        string cipherText = vault.EncryptSecret(originalSecret);
        cipherText.Should().NotBeNullOrEmpty();
        cipherText.Should().NotBe(originalSecret);

        string decrypted = vault.DecryptSecret(cipherText);
        decrypted.Should().Be(originalSecret);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void DpapiSecretVault_EmptyOrNullSecret_ShouldReturnEmpty(string? input)
    {
        var vault = new DpapiSecretVault();
        vault.EncryptSecret(input!).Should().BeEmpty();
        vault.DecryptSecret(input!).Should().BeEmpty();
    }
}
