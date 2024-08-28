using System.Diagnostics;
using System.Text.RegularExpressions;

namespace TraceLineage;

internal class Program
{
    internal static void Main(string[] args)
    {
        var repoRootPath = args.Length > 0 ? args[0] : "path/to/your/repo";
        var folderPath = args.Length > 1 ? args[1] : "path/to/your/folder";
        var filePattern = args.Length > 2 ? args[2] : "*.feature";
        var regexPattern = args.Length > 3 ? args[3] : "your-regex-pattern";
        var outputDirectory = args.Length > 4 ? args[4] : "output";
        var csvOutputFile = args.Length > 5 ? args[5] : "summary.csv";
        var maxCommits = args.Length > 6 ? int.Parse(args[6]) : 100;

        // Ensure the output directory exists
        if (!Directory.Exists(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        // Find all files in the folder that match the given file pattern
        var files = Directory.GetFiles(folderPath, filePattern);

        // Initialize a list to store the summary results
        var results = new List<CommitSummary>();

        foreach (var file in files)
        {
            Console.WriteLine($"Processing file: {file}");

            // Read the file content
            var fileContent = File.ReadAllLines(file);

            // Find all lines that match the regex pattern
            var matchingLines = fileContent
                .Select((line, index) => new { Line = line, LineNumber = index + 1 })
                .Where(x => Regex.IsMatch(x.Line, regexPattern))
                .ToList();

            foreach (var match in matchingLines)
            {
                var lineNumber = match.LineNumber;
                var lineContent = match.Line.Trim();

                // Generate a unique output file name for each matching line
                var outputFileName = $"line_{lineNumber}_{Path.GetFileName(file)}.txt";
                var outputFilePath = Path.Combine(outputDirectory, outputFileName);

                // Call the TraceLineage method for the current matching line
                var commitHistory = TraceLineage(repoRootPath, file, lineContent, maxCommits);
                SaveCommitHistory(commitHistory, outputFilePath);

                // Save the result for this line in the summary list
                var commitCount = commitHistory.Count;
                results.Add(new CommitSummary(file, lineContent, commitCount));

                // Output progress for the current line
                Console.WriteLine($"Processed line {lineNumber} in file {Path.GetFileName(file)}: '{lineContent}'");
                Console.WriteLine($"Number of commits found: {commitCount}");
                Console.WriteLine($"Output saved to: {outputFilePath}");
                Console.WriteLine("----------------------------");
            }
        }

        // Output the summary results to a CSV file
        SaveSummaryToCsv(results, csvOutputFile);

        Console.WriteLine($"Summary saved to CSV file: {csvOutputFile}");
    }

    private static List<CommitEntry> TraceLineage(string repoRootPath, string filePath, string lineContent,
        int maxCommits)
    {
        var git = new GitRunner(repoRootPath);
        
        var commitHistory = new List<CommitEntry>();
        var currentContent = lineContent;
        var currentHash = git.Run("rev-parse HEAD")[0];
        
        for (var i = 0; i < maxCommits; i++)
        {
            var commits = git.Run(
                $@"log -n 1 --pretty=format:%H --pickaxe-regex -S""{currentContent}"" {currentHash} -- ""{filePath}""");

            if (commits.Count == 0)
            {
                break;
            }
            
            var commitHash = commits[0];

            var diff = git.Run($@"--no-pager show {commitHash} -- ""{filePath}""");
            var patchContent = (diff.Count > 0) ? string.Join("\n", diff) : null;
            commitHistory.Add(new(commitHash, currentContent, patchContent ?? "(diff not available)"));

            var previousContentMatch = diff.FindIndex(line => line.StartsWith('+') && line.Contains(currentContent));

            if (previousContentMatch != -1)
            {
                currentContent = diff[previousContentMatch - 1].Substring(1).TrimStart();
            }
            else
            {
                break;
            }
        }

        return commitHistory;
    }

    private record GitRunner(string RepoRootPath)
    {
        public List<string> Run(string command)
        {
            var process = new Process
            {
                StartInfo =
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                    FileName = "git",
                    Arguments = command,
                    WorkingDirectory = RepoRootPath
                }
            };

            
            process.Start();

            var outputLines = new List<string>();
            while (!process.StandardOutput.EndOfStream)
            {
                outputLines.Add(process.StandardOutput.ReadLine()!);
            }
            
            process.WaitForExit();

            return outputLines;
        }
    }
    
    private static void SaveCommitHistory(List<CommitEntry> commitHistory, string outputFile)
    {
        var outputLines = new List<string>();

        foreach (var entry in commitHistory)
        {
            outputLines.Add($"Commit: {entry.Commit}");
            outputLines.Add($"Line Content: {entry.Content}");
            outputLines.Add("Diff:");
            outputLines.Add(entry.Diff);
            outputLines.Add("");
        }

        File.WriteAllLines(outputFile, outputLines);
    }

    private static void SaveSummaryToCsv(List<CommitSummary> results, string csvOutputFile)
    {
        var csvLines = new List<string> { "FilePath,LineContent,CommitHistoryLength" };

        foreach (var result in results)
        {
            csvLines.Add($"{result.FilePath},{result.LineContent},{result.CommitHistoryLength}");
        }

        File.WriteAllLines(csvOutputFile, csvLines);
    }
}

internal record CommitEntry(string Commit, string Content, string Diff);

internal record CommitSummary(string FilePath, string LineContent, int CommitHistoryLength);