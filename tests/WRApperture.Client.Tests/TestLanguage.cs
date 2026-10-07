using System.Runtime.CompilerServices;
using WRApperture.Shared.Localization;

namespace WRApperture.Client.Tests;

/// <summary>Messages are asserted in English, whatever the language of the machine running the tests.</summary>
internal static class TestLanguage
{
    [ModuleInitializer]
    internal static void PinEnglish() => Strings.Use(Strings.DefaultLanguage);
}
