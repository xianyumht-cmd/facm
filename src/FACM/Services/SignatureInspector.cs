using System;
using System.Security.Cryptography.X509Certificates;

namespace FACM.Services
{
    internal static class SignatureInspector
    {
        public static string GetCurrentExecutableSignatureStatus()
        {
            try
            {
                var location = System.Reflection.Assembly.GetExecutingAssembly().Location;
                var certificate = X509Certificate.CreateFromSignedFile(location);
                using (var certificate2 = new X509Certificate2(certificate))
                {
                    return "已签名：" + certificate2.GetNameInfo(X509NameType.SimpleName, false);
                }
            }
            catch
            {
                return "当前构建未签名";
            }
        }
    }
}
