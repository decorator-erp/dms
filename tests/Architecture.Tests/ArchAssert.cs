using NetArchTest.Rules;
using Xunit;

namespace Architecture.Tests;

internal static class ArchAssert
{
    public static void Passes(System.Reflection.Assembly assembly, TestResult result)
    {
        IReadOnlyList<string> failing = result.FailingTypeNames ?? [];
        Assert.True(result.IsSuccessful,
            $"{assembly.GetName().Name}: {string.Join(", ", failing)}");
    }
}
