using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using PSWindowsImageTools.Models;

namespace PSWindowsImageTools.Services
{
    /// <summary>
    /// Validates Unattend XML configuration files against the documented rule
    /// set (R1-R21): well-formedness, root/namespace, settings pass attributes,
    /// component sanity, RunSynchronous/RunAsynchronous ordering, settings
    /// structure and curated common mistakes. Read-only. All rule evaluation,
    /// path building, filtering and report building are pure/internal-static
    /// and unit-tested; the file load in ValidateFile is the only thin surface
    /// (parse failures become the single XML-NotWellFormed issue).
    /// </summary>
    public class UnattendXMLValidationService
    {
        private const string ServiceName = "UnattendXMLValidationService";
        private readonly ModuleCallbacks _callbacks;

        /// <summary>
        /// The namespace Windows Setup requires on the unattend root element
        /// </summary>
        public const string UnattendNamespace = "urn:schemas-microsoft-com:unattend";

        /// <summary>
        /// The valid Windows Setup configuration passes, in setup order
        /// </summary>
        public static readonly string[] ValidPasses =
        {
            "windowsPE",
            "offlineServicing",
            "generalize",
            "specialize",
            "oobeSystem"
        };

        private static readonly string[] ValidArchitectures =
        {
            "x86",
            "amd64",
            "ia64",
            "arm",
            "arm64"
        };

        /// <summary>
        /// Creates the service with explicit callbacks
        /// </summary>
        public UnattendXMLValidationService(ModuleCallbacks? callbacks = null)
        {
            _callbacks = callbacks ?? ModuleCallbacks.Silent;
        }

        /// <summary>
        /// Validates an Unattend XML configuration file. Missing files throw
        /// InvalidOperationException; XML parse failures become a single
        /// XML-NotWellFormed error issue instead of an exception.
        /// </summary>
        public UnattendValidationReport ValidateFile(
            string filePath,
            UnattendValidationSeverity minimumSeverity = UnattendValidationSeverity.Warning)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("File path must be provided", nameof(filePath));
            }

            if (!File.Exists(filePath))
            {
                throw new InvalidOperationException($"Unattend XML file not found: {filePath}");
            }

            _callbacks.Verbose?.Invoke($"Validating Unattend XML configuration: {filePath}");

            var document = new XmlDocument();
            List<UnattendValidationIssue> issues;

            try
            {
                document.Load(filePath);
                issues = AnalyzeDocument(document);
            }
            catch (XmlException ex)
            {
                issues = new List<UnattendValidationIssue>
                {
                    new UnattendValidationIssue
                    {
                        Severity = UnattendValidationSeverity.Error,
                        Pass = string.Empty,
                        ElementPath = "/",
                        Message = $"Unattend XML is not well-formed: {ex.Message}",
                        RuleId = "XML-NotWellFormed"
                    }
                };
                _callbacks.Warning?.Invoke($"Unattend XML is not well-formed: {filePath}");
            }

            var report = BuildReport(filePath, issues, minimumSeverity);

            _callbacks.Verbose?.Invoke(
                $"Validation complete for {filePath}: {report.ErrorCount} error(s), {report.WarningCount} warning(s), IsValid={report.IsValid}");

            return report;
        }

        /// <summary>
        /// Validates an in-memory Unattend XML configuration (no file access)
        /// </summary>
        public UnattendValidationReport ValidateDocument(
            UnattendXMLConfiguration config,
            UnattendValidationSeverity minimumSeverity = UnattendValidationSeverity.Warning)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            _callbacks.Verbose?.Invoke($"Validating in-memory Unattend XML configuration: {config.SourceFilePath}");

            var issues = AnalyzeDocument(config.XmlDocument);
            var report = BuildReport(config.SourceFilePath, issues, minimumSeverity);

            _callbacks.Verbose?.Invoke(
                $"Validation complete: {report.ErrorCount} error(s), {report.WarningCount} warning(s), IsValid={report.IsValid}");

            return report;
        }

        /// <summary>
        /// Runs every validation rule in documented order (R1-R21). Pure.
        /// </summary>
        internal static List<UnattendValidationIssue> AnalyzeDocument(XmlDocument document)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            var issues = new List<UnattendValidationIssue>();
            ValidateRootStructure(document, issues);
            ValidateSettings(document, issues);
            ValidateComponents(document, issues);
            ValidateRunCommands(document, issues);
            ValidateKnownSettings(document, issues);
            return issues;
        }

        /// <summary>
        /// R1-R3: root element presence, name and namespace. The R1 parse
        /// failure is raised by the file loader; this covers the DOM shape.
        /// </summary>
        internal static void ValidateRootStructure(XmlDocument document, List<UnattendValidationIssue> issues)
        {
            var root = document.DocumentElement;
            if (root == null)
            {
                AddIssue(issues, UnattendValidationSeverity.Error, string.Empty, "/",
                    "XML-RootNotUnattend", "Document has no root element; an unattend.xml must have an 'unattend' root element");
                return;
            }

            if (!string.Equals(root.LocalName, "unattend", StringComparison.Ordinal))
            {
                AddIssue(issues, UnattendValidationSeverity.Error, string.Empty, BuildElementPath(root),
                    "XML-RootNotUnattend", $"Root element is '{root.LocalName}'; expected 'unattend'");
                return;
            }

            if (!string.Equals(root.NamespaceURI, UnattendNamespace, StringComparison.Ordinal))
            {
                AddIssue(issues, UnattendValidationSeverity.Error, string.Empty, BuildElementPath(root),
                    "XML-WrongNamespace",
                    $"Root element namespace is '{root.NamespaceURI}'; expected '{UnattendNamespace}' (Windows Setup requires the unattend namespace)");
            }
        }

        /// <summary>
        /// R4-R8: settings pass attributes, valid settings children, stray root
        /// children and empty settings sections.
        /// </summary>
        internal static void ValidateSettings(XmlDocument document, List<UnattendValidationIssue> issues)
        {
            var root = document.DocumentElement;
            if (root == null)
            {
                return;
            }

            if (!string.Equals(root.LocalName, "unattend", StringComparison.Ordinal))
            {
                return;
            }

            foreach (var child in GetElementChildren(root))
            {
                if (!string.Equals(child.LocalName, "settings", StringComparison.Ordinal))
                {
                    AddIssue(issues, UnattendValidationSeverity.Error, ResolvePass(child), BuildElementPath(child),
                        "Root-InvalidChild",
                        $"Element '{child.LocalName}' is directly under the root; only 'settings' elements belong under 'unattend'");
                }
            }

            foreach (var settings in SelectByLocalName(document, "settings"))
            {
                var settingsPath = BuildElementPath(settings);
                var pass = Attr(settings, "pass");

                if (string.IsNullOrEmpty(pass))
                {
                    AddIssue(issues, UnattendValidationSeverity.Error, string.Empty, settingsPath,
                        "Pass-Missing", "settings element has no 'pass' attribute");
                }
                else if (!IsKnownPass(pass))
                {
                    // auditSystem/auditUser are real audit-mode passes, but out of scope
                    // for this tool's deployment flows; flag them as warnings, not errors.
                    var severity = IsAuditPass(pass)
                        ? UnattendValidationSeverity.Warning
                        : UnattendValidationSeverity.Error;
                    AddIssue(issues, severity, pass, settingsPath,
                        "Pass-Unknown",
                        $"Unknown configuration pass '{pass}'; valid deployment passes are: {string.Join(", ", ValidPasses)}");
                }

                var children = GetElementChildren(settings);
                if (children.Count == 0)
                {
                    AddIssue(issues, UnattendValidationSeverity.Warning, pass, settingsPath,
                        "Settings-Empty", "settings element has no component elements");
                }

                foreach (var child in children)
                {
                    if (!string.Equals(child.LocalName, "component", StringComparison.Ordinal))
                    {
                        AddIssue(issues, UnattendValidationSeverity.Error, pass, BuildElementPath(child),
                            "Settings-InvalidChild",
                            $"Element '{child.LocalName}' is a direct child of settings; only 'component' elements are valid");
                    }
                }
            }
        }

        /// <summary>
        /// R9-R13: component name, duplicate and processorArchitecture checks.
        /// </summary>
        internal static void ValidateComponents(XmlDocument document, List<UnattendValidationIssue> issues)
        {
            var seenPerSettings = new Dictionary<XmlElement, HashSet<string>>();

            foreach (var component in SelectByLocalName(document, "component"))
            {
                var componentPath = BuildElementPath(component);
                var pass = ResolvePass(component);
                var name = Attr(component, "name");
                var architecture = Attr(component, "processorArchitecture");

                if (string.IsNullOrEmpty(name))
                {
                    AddIssue(issues, UnattendValidationSeverity.Error, pass, componentPath,
                        "Component-MissingName", "component element has no 'name' attribute");
                }
                else if (!name.StartsWith("Microsoft-Windows-", StringComparison.OrdinalIgnoreCase) ||
                         name.IndexOf(' ') >= 0)
                {
                    AddIssue(issues, UnattendValidationSeverity.Warning, pass, componentPath,
                        "Component-UnknownName",
                        $"Unrecognized component name '{name}'; expected a 'Microsoft-Windows-*' name without whitespace");
                }

                if (string.IsNullOrEmpty(architecture))
                {
                    AddIssue(issues, UnattendValidationSeverity.Warning, pass, componentPath,
                        "Component-MissingArchitecture", "component element has no 'processorArchitecture' attribute");
                }
                else if (!IsKnownArchitecture(architecture))
                {
                    AddIssue(issues, UnattendValidationSeverity.Warning, pass, componentPath,
                        "Component-UnknownArchitecture",
                        $"Unknown processorArchitecture '{architecture}'; valid values are: {string.Join(", ", ValidArchitectures)}");
                }

                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(architecture))
                {
                    continue;
                }

                var settingsParent = component.ParentNode as XmlElement;
                if (settingsParent == null ||
                    !string.Equals(settingsParent.LocalName, "settings", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!seenPerSettings.TryGetValue(settingsParent, out var seen))
                {
                    seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    seenPerSettings[settingsParent] = seen;
                }

                var duplicateKey = name + "|" + architecture;
                if (!seen.Add(duplicateKey))
                {
                    AddIssue(issues, UnattendValidationSeverity.Error, pass, componentPath,
                        "Component-Duplicate",
                        $"Component '{name}' ({architecture}) appears more than once in the same settings section");
                }
            }
        }

        /// <summary>
        /// R14-R19: RunSynchronous/RunAsynchronous command ordering and shape.
        /// Order checks are scoped per RunSynchronous/RunAsynchronous container.
        /// </summary>
        internal static void ValidateRunCommands(XmlDocument document, List<UnattendValidationIssue> issues)
        {
            foreach (var section in SelectByLocalName(document, "RunSynchronous")
                         .Concat(SelectByLocalName(document, "RunAsynchronous")))
            {
                var sectionPath = BuildElementPath(section);
                var pass = ResolvePass(section);
                var isAsynchronous = string.Equals(section.LocalName, "RunAsynchronous", StringComparison.Ordinal);
                var expectedCommandName = isAsynchronous ? "RunAsynchronousCommand" : "RunSynchronousCommand";
                var seenOrders = new HashSet<string>(StringComparer.Ordinal);

                if (isAsynchronous && string.Equals(pass, "windowsPE", StringComparison.OrdinalIgnoreCase))
                {
                    AddIssue(issues, UnattendValidationSeverity.Warning, pass, sectionPath,
                        "Run-InvalidPass", "RunAsynchronous commands are not supported in the windowsPE configuration pass");
                }

                foreach (var command in GetElementChildren(section))
                {
                    if (!string.Equals(command.LocalName, expectedCommandName, StringComparison.Ordinal))
                    {
                        AddIssue(issues, UnattendValidationSeverity.Warning, pass, BuildElementPath(command),
                            "Run-UnknownCommandElement",
                            $"Element '{command.LocalName}' inside {section.LocalName}; expected '{expectedCommandName}'");
                        continue;
                    }

                    var commandPath = BuildElementPath(command);
                    var order = ChildElementText(command, "Order");
                    if (string.IsNullOrWhiteSpace(order))
                    {
                        AddIssue(issues, UnattendValidationSeverity.Error, pass, commandPath,
                            "Run-MissingOrder", $"{expectedCommandName} has no non-empty 'Order' element");
                    }
                    else if (!TryParsePositiveOrder(order, out var orderValue))
                    {
                        AddIssue(issues, UnattendValidationSeverity.Error, pass, commandPath,
                            "Run-InvalidOrder",
                            $"Order value '{order}' is not a positive integer");
                    }
                    else if (!seenOrders.Add(orderValue.ToString(CultureInfo.InvariantCulture)))
                    {
                        AddIssue(issues, UnattendValidationSeverity.Error, pass, commandPath,
                            "Run-DuplicateOrder",
                            $"Duplicate Order {orderValue} within the same {section.LocalName} section; execution order is undefined");
                    }

                    if (string.IsNullOrWhiteSpace(ChildElementText(command, "Command")))
                    {
                        AddIssue(issues, UnattendValidationSeverity.Error, pass, commandPath,
                            "Run-MissingCommand", $"{expectedCommandName} has no non-empty 'Command' element");
                    }
                }
            }
        }

        /// <summary>
        /// R20-R21: curated common-mistake table. CopyProfile is only honored
        /// in the specialize pass; SkipMachineOOBE/SkipUserOOBE are deprecated.
        /// </summary>
        internal static void ValidateKnownSettings(XmlDocument document, List<UnattendValidationIssue> issues)
        {
            foreach (var component in SelectByLocalName(document, "component"))
            {
                var name = Attr(component, "name");
                if (!string.Equals(name, "Microsoft-Windows-Shell-Setup", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var pass = ResolvePass(component);
                var componentPath = BuildElementPath(component);

                foreach (var child in GetElementChildren(component))
                {
                    var childPath = componentPath + "/" + child.LocalName;

                    if (string.Equals(child.LocalName, "CopyProfile", StringComparison.Ordinal))
                    {
                        if (!string.IsNullOrEmpty(pass) &&
                            !string.Equals(pass, "specialize", StringComparison.OrdinalIgnoreCase))
                        {
                            AddIssue(issues, UnattendValidationSeverity.Error, pass, childPath,
                                "Setting-WrongPass",
                                $"CopyProfile is only honored in the 'specialize' pass, not '{pass}'; it is silently ignored elsewhere");
                        }
                    }
                    else if (string.Equals(child.LocalName, "SkipMachineOOBE", StringComparison.Ordinal) ||
                             string.Equals(child.LocalName, "SkipUserOOBE", StringComparison.Ordinal))
                    {
                        AddIssue(issues, UnattendValidationSeverity.Warning, pass, childPath,
                            "Setting-Deprecated",
                            $"'{child.LocalName}' is deprecated and ignored by modern Windows Setup");
                    }
                }
            }
        }

        /// <summary>
        /// Keeps only issues at or above the minimum severity. Pure.
        /// </summary>
        internal static List<UnattendValidationIssue> FilterIssues(
            List<UnattendValidationIssue> issues,
            UnattendValidationSeverity minimumSeverity)
        {
            if (issues == null)
            {
                return new List<UnattendValidationIssue>();
            }

            return issues.Where(i => i.Severity >= minimumSeverity).ToList();
        }

        /// <summary>
        /// Builds the report: IsValid over the complete (unfiltered) issue set,
        /// Issues filtered to the minimum severity. Pure.
        /// </summary>
        internal static UnattendValidationReport BuildReport(
            string filePath,
            List<UnattendValidationIssue> issues,
            UnattendValidationSeverity minimumSeverity)
        {
            var source = issues ?? new List<UnattendValidationIssue>();
            var hasErrors = source.Any(i => i.Severity == UnattendValidationSeverity.Error);

            return new UnattendValidationReport
            {
                FilePath = filePath ?? string.Empty,
                Issues = FilterIssues(source, minimumSeverity),
                IsValid = !hasErrors,
                ValidatedAt = DateTime.UtcNow
            };
        }

        /// <summary>
        /// Builds a readable, stable element path:
        /// /unattend/settings[@pass='specialize']/component[@name='X']/Child.
        /// Pure.
        /// </summary>
        internal static string BuildElementPath(XmlNode? node)
        {
            if (node == null || node.NodeType != XmlNodeType.Element)
            {
                return "/";
            }

            var segments = new List<string>();
            var current = node as XmlElement;
            while (current != null)
            {
                segments.Insert(0, FormatPathSegment(current));
                current = current.ParentNode as XmlElement;
            }

            return "/" + string.Join("/", segments);
        }

        private static string FormatPathSegment(XmlElement element)
        {
            var localName = element.LocalName;

            if (string.Equals(localName, "unattend", StringComparison.Ordinal))
            {
                return "unattend";
            }

            if (string.Equals(localName, "settings", StringComparison.Ordinal))
            {
                var pass = Attr(element, "pass");
                return string.IsNullOrEmpty(pass) ? "settings" : $"settings[@pass='{pass}']";
            }

            if (string.Equals(localName, "component", StringComparison.Ordinal))
            {
                var name = Attr(element, "name");
                return string.IsNullOrEmpty(name) ? "component" : $"component[@name='{name}']";
            }

            var index = 1;
            var sibling = element.PreviousSibling;
            while (sibling != null)
            {
                var siblingElement = sibling as XmlElement;
                if (siblingElement != null &&
                    string.Equals(siblingElement.LocalName, localName, StringComparison.Ordinal))
                {
                    index++;
                }

                sibling = sibling.PreviousSibling;
            }

            return index > 1 ? $"{localName}[{index}]" : localName;
        }

        private static void AddIssue(
            List<UnattendValidationIssue> issues,
            UnattendValidationSeverity severity,
            string pass,
            string elementPath,
            string ruleId,
            string message)
        {
            issues.Add(new UnattendValidationIssue
            {
                Severity = severity,
                Pass = pass ?? string.Empty,
                ElementPath = elementPath ?? string.Empty,
                Message = message,
                RuleId = ruleId
            });
        }

        private static bool IsKnownPass(string pass)
        {
            foreach (var validPass in ValidPasses)
            {
                if (string.Equals(pass, validPass, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsAuditPass(string pass)
        {
            return string.Equals(pass, "auditSystem", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(pass, "auditUser", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsKnownArchitecture(string architecture)
        {
            foreach (var validArchitecture in ValidArchitectures)
            {
                if (string.Equals(architecture, validArchitecture, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryParsePositiveOrder(string text, out int order)
        {
            if (int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out order))
            {
                return order > 0;
            }

            order = 0;
            return false;
        }

        private static List<XmlElement> SelectByLocalName(XmlDocument document, string localName)
        {
            var result = new List<XmlElement>();
            var nodes = document.SelectNodes("//*[local-name()='" + localName + "']");
            if (nodes == null)
            {
                return result;
            }

            foreach (var node in nodes.Cast<XmlNode>())
            {
                if (node is XmlElement element)
                {
                    result.Add(element);
                }
            }

            return result;
        }

        private static List<XmlElement> GetElementChildren(XmlNode node)
        {
            var children = new List<XmlElement>();
            foreach (var child in node.ChildNodes.Cast<XmlNode>())
            {
                if (child is XmlElement element)
                {
                    children.Add(element);
                }
            }

            return children;
        }

        private static string ChildElementText(XmlElement parent, string localName)
        {
            foreach (var child in GetElementChildren(parent))
            {
                if (string.Equals(child.LocalName, localName, StringComparison.Ordinal))
                {
                    return child.InnerText;
                }
            }

            return string.Empty;
        }

        private static string ResolvePass(XmlNode node)
        {
            var current = node.ParentNode;
            while (current != null && current.NodeType == XmlNodeType.Element)
            {
                var element = (XmlElement)current;
                if (string.Equals(element.LocalName, "settings", StringComparison.Ordinal))
                {
                    return Attr(element, "pass");
                }

                current = current.ParentNode;
            }

            return string.Empty;
        }

        private static string Attr(XmlElement element, string name)
        {
            var attribute = element.Attributes[name];
            return attribute?.Value ?? string.Empty;
        }
    }
}
