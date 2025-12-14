using System.Security.Cryptography;
using System.Text;

namespace DomainServices.Services
{
    public class Encyption
    {
        public static string Encrypt(string text, string key)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] keyBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(key));
                using (Aes aesAlg = Aes.Create())
                {
                    aesAlg.Key = keyBytes;
                    aesAlg.Mode = CipherMode.ECB;
                    aesAlg.Padding = PaddingMode.PKCS7;

                    ICryptoTransform encryptor = aesAlg.CreateEncryptor(aesAlg.Key, aesAlg.IV);

                    byte[] textBytes = Encoding.UTF8.GetBytes(text);

                    byte[] encryptedBytes = encryptor.TransformFinalBlock(textBytes, 0, textBytes.Length);
                    return Convert.ToBase64String(encryptedBytes);
                }
            }
        }

        public static string Decrypt(string encryptedText, string key)
        {
            if (string.IsNullOrWhiteSpace(encryptedText))
                throw new ArgumentException("Encrypted text cannot be null or empty", nameof(encryptedText));

            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Encryption key cannot be null or empty", nameof(key));
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] keyBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(key));
                using (Aes aesAlg = Aes.Create())
                {
                    aesAlg.Key = keyBytes;
                    aesAlg.Mode = CipherMode.ECB;
                    aesAlg.Padding = PaddingMode.PKCS7;

                    ICryptoTransform decryptor = aesAlg.CreateDecryptor(aesAlg.Key, aesAlg.IV);

                    byte[] encryptedBytes = Convert.FromBase64String(encryptedText);

                    byte[] decryptedBytes = decryptor.TransformFinalBlock(encryptedBytes, 0, encryptedBytes.Length);
                    return Encoding.UTF8.GetString(decryptedBytes);
                }
            }
        }
    }
}
