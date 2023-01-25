
using SourceGen;

namespace SourceGenConsoleTest;

public class Program
{
	public static void Main(string[] args)
	{
		Console.WriteLine(HogeMoge.BranchName);
	}
}

[GenerateGitInformation]
public partial class HogeMoge
{
}