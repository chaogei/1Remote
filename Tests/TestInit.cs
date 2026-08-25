using _1RM;
using _1RM.Service;
using _1RM.Utils;
using Shawn.Utils.Interface;

namespace Tests
{
    public static class TestInit
    {
        /// <summary>
        /// The least the app's static helpers need before anything under test touches them: a salt for the
        /// string cipher, and a language service that echoes keys back so asserting on translated text does
        /// not depend on which language file happens to be loaded.
        /// </summary>
        public static void Init()
        {
            UnSafeStringEncipher.Init("tests-only-salt");

            IoC.GetByType = (type, key) =>
            {
                if (type == typeof(ILanguageService) || type == typeof(LanguageService) || type == typeof(MockLanguageService))
                    return new MockLanguageService();
                return null;
            };
        }
    }
}
