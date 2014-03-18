using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Xml;

namespace BitMiracle.VisualStudioConverter.CSScript
{
    public class Program
    {
        private static readonly List<string> m_supportedTargets = new List<string>()
        {
            "2005",
            "2008",
            "2010",
            "2012"
        };

        public static int Main(params string[] args)
        {
            try
            {
                CommandLineArguments arguments = new CommandLineArguments(args);
                Options options = createOptions(arguments);
                if (options == null)
                {
                    printUsage();
                    return 1;
                }

                Converter converter = new Converter(options);
                converter.Execute();

                return 0;
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                Console.WriteLine(e.StackTrace);
                return 1;
            }
        }

        private static Options createOptions(CommandLineArguments arguments)
        {
            if (arguments.FreeArguments.Count != 1)
            {
                Console.WriteLine("You must specify exactly 1 path to VS 2010 solution");
                return null;
            }

            Options options = new Options(arguments.FreeArguments[0]);

            if (arguments.NamedArguments.ContainsKey("target"))
            {
                string target = arguments.NamedArguments["target"];
                if (!m_supportedTargets.Contains(target))
                {
                    Console.WriteLine("Invalid target");
                    return null;
                }

                options.Target = target;
            }

            if (arguments.NamedArguments.ContainsKey("output"))
                options.ResultDirectory = arguments.NamedArguments["output"];

            if (arguments.NamedArguments.ContainsKey("solutionpostfix"))
                options.ResultSolutionPostfix = arguments.NamedArguments["solutionpostfix"];

            if (arguments.NamedArguments.ContainsKey("replacesolutionpostfix"))
                options.ReplaceSolutionPostfix = arguments.NamedArguments["replacesolutionpostfix"];

            if (arguments.NamedArguments.ContainsKey("projectpostfix"))
                options.ResultProjectPostfix = arguments.NamedArguments["projectpostfix"];

            if (arguments.NamedArguments.ContainsKey("replaceprojectpostfix"))
                options.ReplaceProjectPostfix = arguments.NamedArguments["replaceprojectpostfix"];

            return options;
        }

        private static void printUsage()
        {
            Console.WriteLine("Arguments:");
            Console.WriteLine("Path to VS 2010 or VS 2012 solution file");
            Console.WriteLine("[/target=2012|2010|2008|2005] - target version of Visual Studio; 2008 by default");
            Console.WriteLine("[/output=...] - where to put converted files; directory with solution by default");
            Console.WriteLine("[/solutionpostfix=...] - postfix added to result solution; no postfix by default");
            Console.WriteLine("[/replacesolutionpostfix=...] - postfix to replace in original solution; no postfix by default");
            Console.WriteLine("[/projectpostfix=...] - postfix added to result projects; no postfix by default");
            Console.WriteLine("[/replaceprojectpostfix=...] - postfix to replace in original projects; no postfix by default");
        }
    }

    class CommandLineArguments
    {
        private List<string> m_freeArguments = new List<string>();
        private Dictionary<string, string> m_namedArguments = new Dictionary<string, string>();

        public CommandLineArguments(params string[] arguments)
        {
            parse(arguments);
        }

        public List<string> FreeArguments
        {
            get { return m_freeArguments; }
        }

        public Dictionary<string, string> NamedArguments
        {
            get { return m_namedArguments; }
        }

        private void parse(string[] arguments)
        {
            foreach (string argument in arguments)
            {
                if (String.IsNullOrEmpty(argument))
                    continue;

                if (argument.StartsWith("/") && argument.Contains("=")) // named
                    processNamedArgument(argument);
                else
                    processFreeArgument(argument);
            }
        }

        private void processNamedArgument(string argument)
        {
            int firstEqualSignIndex = argument.IndexOf('=');
            if (firstEqualSignIndex == 1) // argument start with /=
            {
                processFreeArgument(argument);
                return;
            }

            string name = argument.Substring(1, firstEqualSignIndex - 1);
            string value = argument.Substring(firstEqualSignIndex + 1);
            m_namedArguments.Add(name, value);
        }

        private void processFreeArgument(string argument)
        {
            m_freeArguments.Add(argument);
        }
    }

    public class Options
    {
        private string m_pathToSolution;
        private string m_target = "2008"; // TODO - replace to enum here
        private string m_resultDirectory;
        private string m_resultSolutionPostfix = String.Empty;
        private string m_replaceSolutionPostfix = String.Empty;
        private string m_resultProjectPostfix = String.Empty;
        private string m_replaceProjectPostfix = String.Empty;

        public Options(string pathToVS2010Solution)
        {
            if (String.IsNullOrEmpty(pathToVS2010Solution))
                throw new ArgumentNullException("pathToVS2010Solution");

            m_pathToSolution = pathToVS2010Solution;

            m_resultDirectory = m_pathToSolution; // result directory is the same with directory with solution by default
        }

        public string PathToSolution
        {
            get { return m_pathToSolution; }
        }

        public string Target
        {
            get { return m_target; }
            set { m_target = value; }
        }

        public string ResultDirectory
        {
            get { return m_resultDirectory; }
            set { m_resultDirectory = value; }
        }

        public string ResultSolutionPostfix
        {
            get { return m_resultSolutionPostfix; }
            set { m_resultSolutionPostfix = value; }
        }

        public string ReplaceSolutionPostfix
        {
            get { return m_replaceSolutionPostfix; }
            set { m_replaceSolutionPostfix = value; }
        }

        public string ResultProjectPostfix
        {
            get { return m_resultProjectPostfix; }
            set { m_resultProjectPostfix = value; }
        }

        public string ReplaceProjectPostfix
        {
            get { return m_replaceProjectPostfix; }
            set { m_replaceProjectPostfix = value; }
        }
    }

    class Converter
    {
        private Options m_options;
        private VisualStudioFileFormat m_visualStudioFormat;

        public Converter(Options options)
        {
            if (options == null)
                throw new ArgumentNullException("options");

            m_options = options;
            m_visualStudioFormat = VisualStudioFileFormat.Create(m_options.Target);
        }

        public void Execute()
        {
            if (!File.Exists(m_options.PathToSolution))
                throw new FileNotFoundException("Solution file not found");

            processSolution();
        }

        /// <summary>
        /// Code from http://www.emmet-gray.com/Articles/ProjectConverter.htm
        /// </summary>
        private void processSolution()
        {
            StringCollection solutionLines = new StringCollection();
            byte[] byteOrderMark = parseOriginalSolution(solutionLines);
            writeResultSolution(byteOrderMark, solutionLines);
        }

        private byte[] parseOriginalSolution(StringCollection solutionLines)
        {
            using (FileStream solutionStream = new FileStream(m_options.PathToSolution, FileMode.Open))
            {
                byte[] byteOrderMark = readUnicodeByteOrderMark(solutionStream);
                processSolutionLines(solutionStream, solutionLines);

                return byteOrderMark;
            }
        }

        private void processSolutionLines(FileStream solutionStream, StringCollection solutionLines)
        {
            const string SolutionFormatVersion = "Microsoft Visual Studio Solution File, Format Version";

            StreamReader streamReader = new StreamReader(solutionStream);
            while (streamReader.Peek() >= 0)
            {
                string line = streamReader.ReadLine();

                if (line.StartsWith(SolutionFormatVersion))
                {
                    solutionLines.Add(SolutionFormatVersion + " " + m_visualStudioFormat.SolutionFormatVersion);
                    continue;
                }

                if (line.StartsWith("# Visual"))
                {
                    solutionLines.Add("# Visual Studio " + m_visualStudioFormat.Format);
                    continue;
                }

                if (line.StartsWith("Project("))
                {
                    string processedLine = processProjectLine(line);
                    solutionLines.Add(processedLine);
                    continue;
                }

                solutionLines.Add(line);
            }
        }

        private string processProjectLine(string line)
        {
            Debug.Assert(line.StartsWith("Project("));

            string[] projParts = line.Split(',');
            if (projParts.Length != 3)
                return line;

            string pathToProject = projParts[1].Trim(' ', '"');
            if (String.Compare(pathToProject, "Solution Items", true, CultureInfo.InvariantCulture) == 0)
                return line;

            string projectFile = Path.Combine(Path.GetDirectoryName(m_options.PathToSolution), pathToProject);
            string projectExtension = Path.GetExtension(projectFile);
            if (projectExtension != ".vbproj" && projectExtension != ".csproj") // Currently support only these cases specially. Original code has support of vcproj and vcxproj files.
                return line;

            string resultProjectDirectory = Path.Combine(m_options.ResultDirectory, Path.GetDirectoryName(pathToProject));
            if (!Directory.Exists(resultProjectDirectory))
                Directory.CreateDirectory(resultProjectDirectory);

            string resultProjectFileName = processProjectName(projectFile);
            string resultProjectPath = Path.Combine(resultProjectDirectory, resultProjectFileName);
            File.Copy(projectFile, resultProjectPath, true);
            convertProject(resultProjectPath);

            string projectName = Path.GetFileName(projectFile);

            return String.Format(
                "{0},{1},{2}",
                projParts[0].Replace(Path.GetFileNameWithoutExtension(projectFile), Path.GetFileNameWithoutExtension(resultProjectFileName)),
                projParts[1].Replace(projectName, resultProjectFileName),
                projParts[2]
            );
        }

        private static byte[] readUnicodeByteOrderMark(FileStream solutionStream)
        {
            BinaryReader binaryReader = new BinaryReader(solutionStream);
            // let's read Unicode Byte Order Mark (with CRLF)
            byte[] byteOrderMark = binaryReader.ReadBytes(5);
            // if we don't have a BOM, we create a default one
            if (byteOrderMark[0] != 0xef)
            {
                byteOrderMark[0] = 0xef;
                byteOrderMark[1] = 0xbb;
                byteOrderMark[2] = 0xbf;
                byteOrderMark[3] = 0xd;
                byteOrderMark[4] = 0xa;

                // rewind the streamreaders
                solutionStream.Seek(0, SeekOrigin.Begin);
            }

            return byteOrderMark;
        }

        private void writeResultSolution(byte[] bom, StringCollection sc)
        {
            if (!Directory.Exists(m_options.ResultDirectory))
                Directory.CreateDirectory(m_options.ResultDirectory);

            string resultSolutionName = processSolutionName(m_options.PathToSolution);
            string pathToResultSolution = Path.Combine(m_options.ResultDirectory, resultSolutionName);
            using (FileStream fs = new FileStream(pathToResultSolution, FileMode.Create))
            {
                // write the BOM bytes (using the BinaryWriter)
                BinaryWriter bw = new BinaryWriter(fs);
                bw.Write(bom);
                bw.Flush();

                // write the remaining text (using the StreamWriter)
                StreamWriter sw = new StreamWriter(fs);

                foreach (string buf in sc)
                    sw.WriteLine(buf);

                sw.Flush();
            }
        }

        //
        // Convert the VB and C# Project file
        //
        private void convertProject(string projectFile)
        {
            XmlDocument projectDocument = new XmlDocument();
            projectDocument.Load(projectFile);

            XmlNamespaceManager xmlNamespaceManager = new XmlNamespaceManager(new NameTable());
            xmlNamespaceManager.AddNamespace("prj", "http://schemas.microsoft.com/developer/msbuild/2003");

            if (!processProjectToolsVersion(projectDocument, xmlNamespaceManager))
                return;

            processProductVersion(projectDocument, xmlNamespaceManager);
            removeOldToolsVersion(projectDocument, xmlNamespaceManager);
            processTargetFrameworkVersion(projectDocument, xmlNamespaceManager);
            //processBootStrapper(projectDocument, xmlNamespaceManager);
            processProjectDependencies(projectDocument, xmlNamespaceManager);
            processImportsToolsPath(projectDocument, xmlNamespaceManager);
            processImportsSilverlightVersion(projectDocument, xmlNamespaceManager);

            projectDocument.Save(projectFile);
        }

        private bool processProjectToolsVersion(XmlDocument projectDocument, XmlNamespaceManager xmlNamespaceManager)
        {
            XmlNode projectNode = projectDocument.SelectSingleNode("/prj:Project", xmlNamespaceManager);
            if (projectNode == null)
                throw new ApplicationException("Invalid project file");

            // check is project already in target format
            XmlAttribute toolsVersionAttribute = projectNode.Attributes["ToolsVersion"];
            if (toolsVersionAttribute != null)
            {
                if (toolsVersionAttribute.InnerText == m_visualStudioFormat.ProjectToolsVersion)
                    return false;
            }
            else
            {
                // tools version attribute is absent in VS 2005 projects
                if (m_visualStudioFormat.Format == "2005")
                    return false;
            }

            string toolsVersion = m_visualStudioFormat.ProjectToolsVersion;
            if (!String.IsNullOrEmpty(toolsVersion)) // VS 2008, 2010
                toolsVersionAttribute.Value = toolsVersion;
            else // VS 2005
                projectNode.Attributes.Remove(projectNode.Attributes["ToolsVersion"]);

            return true;
        }

        private void processProductVersion(XmlDocument projectDocument, XmlNamespaceManager xmlNamespaceManager)
        {
            XmlNode productVersionNode = projectDocument.SelectSingleNode("/prj:Project/prj:PropertyGroup/prj:ProductVersion", xmlNamespaceManager);
            if (productVersionNode != null)
                productVersionNode.InnerText = m_visualStudioFormat.ProjectProductVersion;
        }

        private static void removeOldToolsVersion(XmlDocument projectDocument, XmlNamespaceManager xmlNamespaceManager)
        {
            XmlNode oldToolsVersionNode = projectDocument.SelectSingleNode("/prj:Project/prj:PropertyGroup/prj:OldToolsVersion", xmlNamespaceManager);
            if (oldToolsVersionNode != null)
                oldToolsVersionNode.ParentNode.RemoveChild(oldToolsVersionNode);
        }

        private void processTargetFrameworkVersion(XmlDocument projectDocument, XmlNamespaceManager xmlNamespaceManager)
        {
            XmlNodeList targetFrameworkVersionNodes = projectDocument.SelectNodes("/prj:Project/prj:PropertyGroup/prj:TargetFrameworkVersion", xmlNamespaceManager);
            foreach (XmlNode targetFrameworkVersionNode in targetFrameworkVersionNodes)
            {
                if (targetFrameworkVersionNode != null)
                {
                    if (m_options.Target == "2005")
                    {
                        targetFrameworkVersionNode.ParentNode.RemoveChild(targetFrameworkVersionNode);
                    }
                    else if (m_options.Target == "2008")
                    {
                        if (!isFrameworkVersionSupportedByVS2008(targetFrameworkVersionNode.InnerText))
                            targetFrameworkVersionNode.InnerText = "v3.5";
                    }
                }
                else
                {
                    switch (m_options.Target)
                    {
                        case "2008":
                        case "2010":
                            XmlNode newTargetFrameworkVersionNode = projectDocument.CreateElement("TargetFrameworkVersion", xmlNamespaceManager.LookupNamespace("prj"));
                            newTargetFrameworkVersionNode.AppendChild(projectDocument.CreateTextNode("v2.0"));

                            XmlNode propertyGroupNode = projectDocument.SelectSingleNode("/prj:Project/prj:PropertyGroup", xmlNamespaceManager);
                            propertyGroupNode.AppendChild(newTargetFrameworkVersionNode);
                            break;
                    }
                }
            }
        }

        private static bool isFrameworkVersionSupportedByVS2008(string targetFrameworkVersion)
        {
            string[] supportedFrameworkVersionsByVS2008 = { "v1.0", "v1.1", "v2.0", "v3.0", "v3.5" };
            foreach (string supportedFrameworkVersion in supportedFrameworkVersionsByVS2008)
            {
                if (targetFrameworkVersion == supportedFrameworkVersion)
                    return true;
            }

            return false;
        }

        private void processBootStrapper(XmlDocument projectDocument, XmlNamespaceManager xmlNamespaceManager)
        {
            // the optional BootStrapper elements.  I only remove the inappropriate values, I
            // do not attempt to add the newer values
            XmlNode xn = default(XmlNode);
            switch (m_options.Target)
            {
                case "2005":
                    // alter the value for the ProductName element using the older framework tag
                    xn = projectDocument.SelectSingleNode("/prj:Project/prj:ItemGroup/prj:BootstrapperPackage" + "[@Include=\"Microsoft.Net.Framework.2.0\"]", xmlNamespaceManager);
                    if (xn != null)
                    {
                        XmlNode xtemp = xn.SelectSingleNode("prj:ProductName", xmlNamespaceManager);
                        xtemp.FirstChild.Value = ".NET Framework 2.0";
                    }

                    // remove the newer framework options
                    xn = projectDocument.SelectSingleNode("/prj:Project/prj:ItemGroup/prj:BootstrapperPackage" + "[@Include=\"Microsoft.Net.Framework.3.0\"]", xmlNamespaceManager);
                    if ((xn != null))
                        xn.ParentNode.RemoveChild(xn);

                    xn = projectDocument.SelectSingleNode("/prj:Project/prj:ItemGroup/prj:BootstrapperPackage" + "[@Include=\"Microsoft.Net.Framework.3.5\"]", xmlNamespaceManager);
                    if ((xn != null))
                        xn.ParentNode.RemoveChild(xn);

                    xn = projectDocument.SelectSingleNode("/prj:Project/prj:ItemGroup/prj:BootstrapperPackage" + "[@Include=\"Microsoft.Net.Client.3.5\"]", xmlNamespaceManager);
                    if ((xn != null))
                        xn.ParentNode.RemoveChild(xn);

                    xn = projectDocument.SelectSingleNode("/prj:Project/prj:ItemGroup/prj:BootstrapperPackage" + "[@Include=\"Microsoft.Net.Framework.3.5.SP1\"]", xmlNamespaceManager);
                    if ((xn != null))
                        xn.ParentNode.RemoveChild(xn);

                    xn = projectDocument.SelectSingleNode("/prj:Project/prj:ItemGroup/prj:BootstrapperPackage" + "[@Include=\"Microsoft.Windows.Installer.3.1\"]", xmlNamespaceManager);
                    if ((xn != null))
                        xn.ParentNode.RemoveChild(xn);

                    break;
                case "2008":
                    // alter the value for the ProjectName using the newer framework tag
                    xn = projectDocument.SelectSingleNode("/prj:Project/prj:ItemGroup/prj:BootstrapperPackage" + "[@Include=\"Microsoft.Net.Framework.2.0\"]", xmlNamespaceManager);
                    if (xn != null)
                    {
                        XmlNode xtemp = xn.SelectSingleNode("prj:ProductName", xmlNamespaceManager);
                        xtemp.FirstChild.Value = ".NET Framework 2.0 %28x86%29";
                    }

                    // remove the newer framework options
                    xn = projectDocument.SelectSingleNode("/prj:Project/prj:ItemGroup/prj:BootstrapperPackage" + "[@Include=\"Microsoft.Net.Client.3.5\"]", xmlNamespaceManager);
                    if ((xn != null))
                        xn.ParentNode.RemoveChild(xn);

                    xn = projectDocument.SelectSingleNode("/prj:Project/prj:ItemGroup/prj:BootstrapperPackage" + "[@Include=\"Microsoft.Net.Framework.3.5.SP1\"]", xmlNamespaceManager);
                    if ((xn != null))
                        xn.ParentNode.RemoveChild(xn);

                    xn = projectDocument.SelectSingleNode("/prj:Project/prj:ItemGroup/prj:BootstrapperPackage" + "[@Include=\"Microsoft.Windows.Installer.3.1\"]", xmlNamespaceManager);
                    if ((xn != null))
                        xn.ParentNode.RemoveChild(xn);

                    break;
                case "2010":
                    // alter the value for the ProjectName using the newer framework tag
                    xn = projectDocument.SelectSingleNode("/prj:Project/prj:ItemGroup/prj:BootstrapperPackage" + "[@Include=\"Microsoft.Net.Framework.2.0\"]", xmlNamespaceManager);
                    if (xn != null)
                    {
                        XmlNode xtemp = xn.SelectSingleNode("prj:ProductName", xmlNamespaceManager);
                        xtemp.FirstChild.Value = ".NET Framework 2.0 %28x86%29";
                    }
                    break;
            }
        }

        private void processProjectDependencies(XmlDocument projectDocument, XmlNamespaceManager xmlNamespaceManager)
        {
            // simply append project postfix to all projects from which this project depends.
            // in future we can replace this implementation to more general (at least append only processed projects)
            foreach (XmlNode xnode in projectDocument.SelectNodes("/prj:Project/prj:ItemGroup/prj:ProjectReference", xmlNamespaceManager))
            {
                XmlAttribute includeAttribute = xnode.Attributes["Include"];
                if (includeAttribute == null)
                    continue;

                foreach (string extension in new string[2] { ".csproj", ".vbproj" })
                {
                    string includeValue = includeAttribute.Value;
                    if (includeValue.EndsWith(extension))
                    {
                        string projectName = Path.GetFileName(includeValue);
                        string resultProjectName = processProjectName(projectName);
                        includeAttribute.Value = includeValue.Replace(projectName, resultProjectName);
                    }
                }
            }
        }

        private void processImportsToolsPath(XmlDocument projectDocument, XmlNamespaceManager xmlNamespaceManager)
        {
            foreach (XmlNode xnode in projectDocument.SelectNodes("/prj:Project/prj:Import", xmlNamespaceManager))
            {
                XmlAttribute projectAttribute = xnode.Attributes["Project"];
                if (projectAttribute == null)
                    continue;

                if (m_options.Target != "2005")
                    projectAttribute.Value = projectAttribute.Value.Replace("MSBuildBinPath", "MSBuildToolsPath");
                else
                    projectAttribute.Value = projectAttribute.Value.Replace("MSBuildToolsPath", "MSBuildBinPath");
            }
        }

        private void processImportsSilverlightVersion(XmlDocument projectDocument, XmlNamespaceManager xmlNamespaceManager)
        {
            // for conversion from VS2010 -> VS2008 only
            if (m_visualStudioFormat.Format != "2008")
                return;

            const string silverlightVersionForVS2008 = "v3.0";

            foreach (XmlNode xnode in projectDocument.SelectNodes("/prj:Project/prj:Import", xmlNamespaceManager))
            {
                XmlAttribute projectAttribute = xnode.Attributes["Project"];
                if (projectAttribute == null)
                    continue;

                if (projectAttribute.Value.Contains(@"Microsoft\Silverlight"))
                {
                    projectAttribute.Value = projectAttribute.Value.Replace("v4.0", silverlightVersionForVS2008)
                                                                   .Replace("$(SilverlightVersion)", silverlightVersionForVS2008);
                }
            }
        }

        private string processSolutionName(string solutionFileName)
        {
            string solutionNameWithoutExt = Path.GetFileNameWithoutExtension(m_options.PathToSolution);
            if (!String.IsNullOrEmpty(m_options.ReplaceSolutionPostfix))
                return solutionNameWithoutExt.Replace(m_options.ReplaceSolutionPostfix, m_options.ResultSolutionPostfix) + ".sln";
            else
                return solutionNameWithoutExt + m_options.ResultSolutionPostfix + ".sln";
        }

        private string processProjectName(string projectFileName)
        {
            string projectNameWithoutExtension = Path.GetFileNameWithoutExtension(projectFileName);
            string projectExtension = Path.GetExtension(projectFileName);

            if (!String.IsNullOrEmpty(m_options.ReplaceProjectPostfix))
                return projectNameWithoutExtension.Replace(m_options.ReplaceProjectPostfix, m_options.ResultProjectPostfix) + projectExtension;
            else
                return projectNameWithoutExtension + m_options.ResultProjectPostfix + projectExtension;
        }
    }

    abstract class VisualStudioFileFormat
    {
        public abstract string Format { get; }
        public abstract string SolutionFormatVersion { get; }

        public abstract string ProjectToolsVersion { get; }
        public abstract string ProjectProductVersion { get; }

        public static VisualStudioFileFormat Create(string visualStudioYear)
        {
            switch (visualStudioYear)
            {
                case "2005": return new VisualStudio2005FileFormat();
                case "2008": return new VisualStudio2008FileFormat();
                case "2010": return new VisualStudio2010FileFormat();
                case "2012": return new VisualStudio2012FileFormat();
                default: throw new InvalidOperationException("Unknown Visual Studio version");
            }
        }
    }

    class VisualStudio2005FileFormat : VisualStudioFileFormat
    {
        public override string Format
        {
            get { return "2005"; }
        }

        public override string SolutionFormatVersion
        {
            get { return "9.00"; }
        }

        public override string ProjectToolsVersion
        {
            get { return ""; }
        }

        public override string ProjectProductVersion
        {
            get { return "8.0.50727"; }
        }
    }

    class VisualStudio2008FileFormat : VisualStudioFileFormat
    {
        public override string Format
        {
            get { return "2008"; }
        }

        public override string SolutionFormatVersion
        {
            get { return "10.00"; }
        }

        public override string ProjectToolsVersion
        {
            get { return "3.5"; }
        }

        public override string ProjectProductVersion
        {
            get { return "9.0.21022"; }
        }
    }

    class VisualStudio2010FileFormat : VisualStudioFileFormat
    {
        public override string Format
        {
            get { return "2010"; }
        }

        public override string SolutionFormatVersion
        {
            get { return "11.00"; }
        }

        public override string ProjectToolsVersion
        {
            get { return "4.0"; }
        }

        public override string ProjectProductVersion
        {
            get { return ""; }
        }
    }

    class VisualStudio2012FileFormat : VisualStudioFileFormat
    {
        public override string Format
        {
            get { return "2012"; }
        }

        public override string SolutionFormatVersion
        {
            get { return "12.00"; }
        }

        public override string ProjectToolsVersion
        {
            get { return "4.0"; }
        }

        public override string ProjectProductVersion
        {
            get { return ""; }
        }
    }
}
