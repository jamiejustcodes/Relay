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

    [Fact]
    public void DpapiSecretVault_ExistingSavedKey_ShouldDecryptSuccessfully()
    {
        var vault = new DpapiSecretVault();
        string existingCipher = "AQAAANCMnd8BFdERjHoAwE/Cl+sBAAAAj2zcY5n7FUy05r/lFPLsxAAAAAACAAAAAAAQZgAAAAEAACAAAAB3wJlDpk/Jg7VTZ/IK2d0Bi4rFCGBbqwPnjD0Ft67g4wAAAAAOgAAAAAIAACAAAAAGS9fmzMXfkDJ9RfH6Mq8tealGiPo6YWloZFruf4DjVDAAAACdxWTWH1RFv0bHB23qOjf/DsJXzxah+JFynXSS+TozmnRA/2WNf1+ZU8UqjkLi9OpAAAAAORlxdt4OBPwtXg4f3GwzAKOXyQZZ6woirLPU/qPm6Gm4yPZzxK0mAXIjC9Qbam1N5IhQButSr7Y7tYtsPM68XQ==";
        string decrypted = vault.DecryptSecret(existingCipher);
        decrypted.Should().StartWith("AIzaSy");
    }
}
