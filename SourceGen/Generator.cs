﻿/*
 * Copyright (c) 2023 Yuto Inoue All Rights Reserved.
 */

using System.Diagnostics;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SourceGen;

[Generator(LanguageNames.CSharp)]
public class GitInformationGenerator : IIncrementalGenerator
{
	public void Initialize(IncrementalGeneratorInitializationContext context)
	{
		context.RegisterPostInitializationOutput(
			static context =>
			{
				context.AddSource(
					"GitVersionSourceGenerationAttribute.cs",
					"""
					namespace SourceGen;

					using System;

					[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
					internal sealed class GenerateGitInformationAttribute : Attribute
					{
					}
					""");
			});

		var source = context.SyntaxProvider.ForAttributeWithMetadataName(
			"SourceGen.GenerateGitInformationAttribute",
			(_, _) => true,
			(syntaxContext, _) => syntaxContext);

		context.RegisterSourceOutput(source, Emit);
	}

	private static void Emit(SourceProductionContext context, GeneratorAttributeSyntaxContext source)
	{
		var typeNode = (TypeDeclarationSyntax)source.TargetNode;
		var typeSymbol = (INamedTypeSymbol)source.TargetSymbol;

		var ns = typeSymbol.ContainingNamespace.IsGlobalNamespace ? "" : $"namespace {typeSymbol.ContainingNamespace};";

		var fullType = typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
			.Replace("global::", "")
			.Replace("<", "_")
			.Replace(">", "_");

		if (!IsGitDirectory())
		{
			context.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.NoUsableGit(), typeNode.Identifier.GetLocation(), typeSymbol.Name));
		}

		var code =
			$$"""
			// <auto-generated/>
			{{ns}}
	
			partial class {{typeSymbol.Name}}
			{
				public static string BranchName => "{{GetBranchName()}}";
				public static string Hash => "{{GetCommitHash()}}";
				public static string[] Tags => new[] { {{string.Join(", ", GetTags(GetCommitHash()).Select(t => string.Format("\"{0}\"", t)))}} };
			}
			""" ;

		context.AddSource($"{fullType}.GitInformationGenerator.g.cs", code);

		bool IsGitDirectory()
		{
			var (_, stdErr, _) = RunCommand("git status");
			return stdErr.Length <= 0;
		}

		string GetBranchName()
		{
			var (stdOut, stdErr, exitCode) = RunCommand("git branch --contains=HEAD");
			if (stdErr.Length > 0 || exitCode != 0)
			{
				return "";
			}

			return stdOut.Remove(0, 1).Trim(); // "* master\r\n" のように出力されるため
		}

		string GetCommitHash()
		{
			var (stdOut, stdErr, exitCode) = RunCommand("git show --format='%H' --no-patch");
			if (stdErr.Length > 0 || exitCode != 0)
			{
				return "";
			}

			return stdOut.Trim();
		}

		string GetTags(string commitHash)
		{
			var (stdOut, stdErr, exitCode) = RunCommand($"git tag -l --contains {commitHash}");
			if (stdErr.Length > 0 || exitCode != 0)
			{
				return "";
			}

			return stdOut.Trim();
		}
	}

	private static (string, string, int) RunCommand(string command)
	{
		var splitted = command.Split(' ');
		var processStartInfo = new ProcessStartInfo(splitted.ElementAt(0), string.Join(" ", splitted.Skip(1)))
		{
			CreateNoWindow = true,
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
		};

		var process = Process.Start(processStartInfo) ?? throw new Exception("Could not start the process.");
		process.WaitForExit();

		string? tmp;
		var stdOut = new StringBuilder();
		while ((tmp = process.StandardOutput.ReadLine() ?? null) != null)
		{
			stdOut.AppendLine(tmp);
		}

		var stdErr = new StringBuilder();
		while ((tmp = process.StandardError.ReadLine() ?? null) != null)
		{
			stdErr.AppendLine(tmp);
		}

		return (stdOut.ToString(), stdErr.ToString(), process.ExitCode);
	}

	public static class DiagnosticDescriptors
	{
		const string CATEGORY = "GitInformation";

		public static DiagnosticDescriptor NoUsableGit() => new(
			id: "GITINFO01",
			title: "No git available or not a git repository",
			messageFormat: "Git is not available on this computer or the Git repository has not been initialized. All fields return an empty string.",
			category: CATEGORY,
			defaultSeverity: DiagnosticSeverity.Warning,
			isEnabledByDefault: true);
	}
}
