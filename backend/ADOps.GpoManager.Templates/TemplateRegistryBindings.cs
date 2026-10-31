using ADOps.GpoManager.Core.Models;

namespace ADOps.GpoManager.Templates;

/// <summary>
/// Liaisons exécutables des 16 modèles du catalogue. Chaque entrée indique la
/// clé de registre, le type ADSI, la valeur et la condition de paramètre.
/// Les chaînes correspondent exactement aux valeurs d'énumération des JSON.
/// </summary>
public static class TemplateRegistryBindings
{
    private const string Explorer = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer";
    private const string UserSystem = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Policies\System";

    public static IReadOnlyDictionary<string, TemplateRegistryBinding> All { get; } = Build();

    private static Dictionary<string, TemplateRegistryBinding> Build()
        => new(StringComparer.OrdinalIgnoreCase)
        {
            ["removable-storage-restrictions"] = new TemplateRegistryBinding
            {
                TemplateId = "removable-storage-restrictions",
                Category = "Périphériques",
                DefaultParameters = Defaults(("access", "Bloqué")),
                Entries = new[]
                {
                    Reg(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\RemovableStorageDevices\{53f56307-b6bf-11d0-94f2-00a0c91efb8b}",
                        "Deny_All", "DWord", "1", whenParameter: "access", whenEquals: "Bloqué"),
                    Reg(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\RemovableStorageDevices\{53f56307-b6bf-11d0-94f2-00a0c91efb8b}",
                        "Deny_All", "DWord", "0", whenParameter: "access", whenEquals: "Lecture seule"),
                    Reg(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\RemovableStorageDevices\{53f56307-b6bf-11d0-94f2-00a0c91efb8b}",
                        "Deny_Write", "DWord", "1", whenParameter: "access", whenEquals: "Lecture seule"),
                    Reg(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\RemovableStorageDevices\{53f56307-b6bf-11d0-94f2-00a0c91efb8b}",
                        "Deny_All", "DWord", "0", whenParameter: "access", whenEquals: "Accès complet"),
                    Reg(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\RemovableStorageDevices\{53f56307-b6bf-11d0-94f2-00a0c91efb8b}",
                        "Deny_Write", "DWord", "0", whenParameter: "access", whenEquals: "Accès complet"),
                },
            },

            ["control-panel-settings"] = new TemplateRegistryBinding
            {
                TemplateId = "control-panel-settings",
                Category = "Restrictions",
                DefaultParameters = Defaults(("blockControlPanel", "true")),
                Entries = new[]
                {
                    Reg(Explorer, "NoControlPanel", "DWord", "1",
                        whenParameter: "blockControlPanel", whenTrue: true),
                },
            },

            ["shell-restrictions"] = new TemplateRegistryBinding
            {
                TemplateId = "shell-restrictions",
                Category = "Restrictions",
                DefaultParameters = Defaults(
                    ("disableRunDialog", "true"),
                    ("disableWinKeys", "true"),
                    ("hidePowerCommands", "false"),
                    ("disableLockWorkstation", "false")),
                Entries = new[]
                {
                    Reg(Explorer, "NoRun", "DWord", "1",
                        whenParameter: "disableRunDialog", whenTrue: true),
                    Reg(Explorer, "NoWinKeys", "DWord", "1",
                        whenParameter: "disableWinKeys", whenTrue: true),
                    Reg(Explorer, "NoClose", "DWord", "1",
                        whenParameter: "hidePowerCommands", whenTrue: true),
                    Reg(UserSystem, "DisableLockWorkstation", "DWord", "1",
                        whenParameter: "disableLockWorkstation", whenTrue: true),
                },
            },

            ["cmd-registry-block"] = new TemplateRegistryBinding
            {
                TemplateId = "cmd-registry-block",
                Category = "Restrictions",
                DefaultParameters = Defaults(
                    ("cmdMode", "Interdit (scripts inclus)"),
                    ("disableRegistryTools", "true")),
                Entries = new[]
                {
                    Reg(UserSystem, "DisableCMD", "DWord", "2",
                        whenParameter: "cmdMode", whenEquals: "Interdit (scripts inclus)"),
                    Reg(UserSystem, "DisableCMD", "DWord", "1",
                        whenParameter: "cmdMode", whenEquals: "Interdit (cmd seule)"),
                    Reg(UserSystem, "DisableRegistryTools", "DWord", "1",
                        whenParameter: "disableRegistryTools", whenTrue: true),
                },
            },

            ["screensaver-lock"] = new TemplateRegistryBinding
            {
                TemplateId = "screensaver-lock",
                Category = "Sessions",
                DefaultParameters = Defaults(("timeoutSeconds", "600")),
                Entries = new[]
                {
                    Reg(@"HKCU\Software\Policies\Microsoft\Windows\Control Panel\Desktop",
                        "ScreenSaveActive", "String", "1"),
                    Reg(@"HKCU\Software\Policies\Microsoft\Windows\Control Panel\Desktop",
                        "ScreenSaverIsSecure", "String", "1"),
                    Reg(@"HKCU\Software\Policies\Microsoft\Windows\Control Panel\Desktop",
                        "ScreenSaveTimeOut", "String", "${timeoutSeconds}"),
                    Reg(@"HKCU\Software\Policies\Microsoft\Windows\Control Panel\Desktop",
                        "SCRNSAVE.EXE", "String", @"C:\Windows\System32\scrnsave.scr"),
                },
            },

            ["autorun-hardening"] = new TemplateRegistryBinding
            {
                TemplateId = "autorun-hardening",
                Category = "Périphériques",
                DefaultParameters = Defaults(("disableAutorun", "true")),
                Entries = new[]
                {
                    Reg(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer",
                        "NoAutorun", "DWord", "1", whenParameter: "disableAutorun", whenTrue: true),
                    Reg(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer",
                        "NoDriveTypeAutoRun", "DWord", "255", whenParameter: "disableAutorun", whenTrue: true),
                },
            },

            ["powershell-execution-policy"] = new TemplateRegistryBinding
            {
                TemplateId = "powershell-execution-policy",
                Category = "Applications",
                DefaultParameters = Defaults(("executionPolicy", "RemoteSigned")),
                Entries = new[]
                {
                    Reg(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\PowerShell",
                        "EnableScripts", "DWord", "0", whenParameter: "executionPolicy", whenEquals: "Désactivé"),
                    Reg(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\PowerShell",
                        "EnableScripts", "DWord", "1", whenParameter: "executionPolicy", whenNotEquals: "Désactivé"),
                    Reg(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\PowerShell",
                        "ExecutionPolicy", "String", "${executionPolicy}", whenParameter: "executionPolicy", whenNotEquals: "Désactivé"),
                },
            },

            ["uac-hardening"] = new TemplateRegistryBinding
            {
                TemplateId = "uac-hardening",
                Category = "Système & sécurité",
                DefaultParameters = Defaults(
                    ("secureDesktop", "true"),
                    ("standardUserBehavior", "Refuser automatiquement")),
                Entries = new[]
                {
                    Reg(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System",
                        "EnableLUA", "DWord", "1"),
                    Reg(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System",
                        "ConsentPromptBehaviorAdmin", "DWord", "2"),
                    Reg(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System",
                        "PromptOnSecureDesktop", "DWord", "1", whenParameter: "secureDesktop", whenTrue: true),
                    Reg(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System",
                        "PromptOnSecureDesktop", "DWord", "0", whenParameter: "secureDesktop", whenTrue: false),
                    Reg(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System",
                        "ConsentPromptBehaviorUser", "DWord", "0",
                        whenParameter: "standardUserBehavior", whenEquals: "Refuser automatiquement"),
                    Reg(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System",
                        "ConsentPromptBehaviorUser", "DWord", "3",
                        whenParameter: "standardUserBehavior", whenEquals: "Demander les informations d'identification"),
                },
            },

            ["smartscreen-enforcement"] = new TemplateRegistryBinding
            {
                TemplateId = "smartscreen-enforcement",
                Category = "Système & sécurité",
                DefaultParameters = Defaults(("level", "Block")),
                Entries = new[]
                {
                    Reg(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\System",
                        "EnableSmartScreen", "DWord", "1"),
                    Reg(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\System",
                        "ShellSmartScreenLevel", "String", "${level}"),
                },
            },

            ["legacy-protocols-hardening"] = new TemplateRegistryBinding
            {
                TemplateId = "legacy-protocols-hardening",
                Category = "Réseau",
                DefaultParameters = Defaults(
                    ("disableSmb1", "true"),
                    ("disableLlmnr", "true"),
                    ("enforceNtlmv2", "true")),
                Entries = new[]
                {
                    Reg(@"HKLM\SYSTEM\CurrentControlSet\Services\LanmanServer\Parameters",
                        "SMB1", "DWord", "0", whenParameter: "disableSmb1", whenTrue: true),
                    Reg(@"HKLM\SYSTEM\CurrentControlSet\Services\mrxsmb10",
                        "Start", "DWord", "4", whenParameter: "disableSmb1", whenTrue: true),
                    Reg(@"HKLM\SOFTWARE\Policies\Microsoft\Windows NT\DNSClient",
                        "EnableMulticast", "DWord", "0", whenParameter: "disableLlmnr", whenTrue: true),
                    Reg(@"HKLM\SYSTEM\CurrentControlSet\Control\Lsa",
                        "LmCompatibilityLevel", "DWord", "5", whenParameter: "enforceNtlmv2", whenTrue: true),
                },
            },

            ["rdp-lockdown"] = new TemplateRegistryBinding
            {
                TemplateId = "rdp-lockdown",
                Category = "Réseau",
                DefaultParameters = Defaults(("mode", "Désactivé")),
                Entries = new[]
                {
                    Reg(@"HKLM\SYSTEM\CurrentControlSet\Control\Terminal Server",
                        "fDenyTSConnections", "DWord", "1", whenParameter: "mode", whenEquals: "Désactivé"),
                    Reg(@"HKLM\SYSTEM\CurrentControlSet\Control\Terminal Server",
                        "fDenyTSConnections", "DWord", "0", whenParameter: "mode", whenEquals: "NLA requis"),
                    Reg(@"HKLM\SOFTWARE\Policies\Microsoft\Windows NT\Terminal Services",
                        "UserAuthentication", "DWord", "1", whenParameter: "mode", whenEquals: "NLA requis"),
                },
            },

            ["windows-defender-firewall"] = new TemplateRegistryBinding
            {
                TemplateId = "windows-defender-firewall",
                Category = "Système & sécurité",
                DefaultParameters = Defaults(
                    ("firewallEnabled", "true"),
                    ("realTimeProtectionEnabled", "true")),
                Entries = new[]
                {
                    Reg(@"HKLM\SOFTWARE\Policies\Microsoft\WindowsFirewall\DomainProfile",
                        "EnableFirewall", "DWord", "1", whenParameter: "firewallEnabled", whenTrue: true),
                    Reg(@"HKLM\SOFTWARE\Policies\Microsoft\WindowsFirewall\DomainProfile",
                        "EnableFirewall", "DWord", "0", whenParameter: "firewallEnabled", whenTrue: false),
                    Reg(@"HKLM\SOFTWARE\Policies\Microsoft\WindowsFirewall\StandardProfile",
                        "EnableFirewall", "DWord", "1", whenParameter: "firewallEnabled", whenTrue: true),
                    Reg(@"HKLM\SOFTWARE\Policies\Microsoft\WindowsFirewall\StandardProfile",
                        "EnableFirewall", "DWord", "0", whenParameter: "firewallEnabled", whenTrue: false),
                    Reg(@"HKLM\SOFTWARE\Policies\Microsoft\Windows Defender",
                        "DisableAntiSpyware", "DWord", "0", whenParameter: "realTimeProtectionEnabled", whenTrue: true),
                    Reg(@"HKLM\SOFTWARE\Policies\Microsoft\Windows Defender",
                        "DisableAntiSpyware", "DWord", "1", whenParameter: "realTimeProtectionEnabled", whenTrue: false),
                    Reg(@"HKLM\SOFTWARE\Policies\Microsoft\Windows Defender\Real-Time Protection",
                        "DisableRealtimeMonitoring", "DWord", "0", whenParameter: "realTimeProtectionEnabled", whenTrue: true),
                    Reg(@"HKLM\SOFTWARE\Policies\Microsoft\Windows Defender\Real-Time Protection",
                        "DisableRealtimeMonitoring", "DWord", "1", whenParameter: "realTimeProtectionEnabled", whenTrue: false),
                },
            },

            ["windows-update"] = new TemplateRegistryBinding
            {
                TemplateId = "windows-update",
                Category = "Maintenance",
                DefaultParameters = Defaults(
                    ("mode", "Windows Update"),
                    ("wuServerUrl", "http://srv-wsus.demo.local:8530"),
                    ("wuStatusServerUrl", "http://srv-wsus.demo.local:8530"),
                    ("auOptions", "4")),
                Entries = new[]
                {
                    Reg(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU",
                        "NoAutoUpdate", "DWord", "0"),
                    Reg(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU",
                        "AUOptions", "DWord", "${auOptions}"),
                    Reg(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate",
                        "WUServer", "String", "${wuServerUrl}", whenParameter: "mode", whenEquals: "WSUS"),
                    Reg(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate",
                        "WUStatusServer", "String", "${wuStatusServerUrl}", whenParameter: "mode", whenEquals: "WSUS"),
                    Reg(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate",
                        "UseWUServer", "DWord", "1", whenParameter: "mode", whenEquals: "WSUS"),
                },
            },

            ["browser-url-blocklist"] = new TemplateRegistryBinding
            {
                TemplateId = "browser-url-blocklist",
                Category = "Applications",
                DefaultParameters = Defaults(
                    ("browser", "Edge"),
                    ("blockedUrls", "https://jeux.exemple.com\nhttps://paris.exemple.com"),
                    ("allowedUrls", "https://intranet.exemple.com")),
                Entries = new[]
                {
                    UrlList(@"HKLM\SOFTWARE\Policies\Microsoft\Edge", "URLBlocklist", "blockedUrls", "Edge"),
                    UrlList(@"HKLM\SOFTWARE\Policies\Microsoft\Edge", "URLAllowlist", "allowedUrls", "Edge"),
                    UrlList(@"HKLM\SOFTWARE\Policies\Microsoft\Edge", "URLBlocklist", "blockedUrls", "Les deux"),
                    UrlList(@"HKLM\SOFTWARE\Policies\Microsoft\Edge", "URLAllowlist", "allowedUrls", "Les deux"),
                    UrlList(@"HKLM\SOFTWARE\Policies\Google\Chrome", "URLBlocklist", "blockedUrls", "Chrome"),
                    UrlList(@"HKLM\SOFTWARE\Policies\Google\Chrome", "URLAllowlist", "allowedUrls", "Chrome"),
                    UrlList(@"HKLM\SOFTWARE\Policies\Google\Chrome", "URLBlocklist", "blockedUrls", "Les deux"),
                    UrlList(@"HKLM\SOFTWARE\Policies\Google\Chrome", "URLAllowlist", "allowedUrls", "Les deux"),
                },
            },

            ["asr-exploit-guard"] = new TemplateRegistryBinding
            {
                TemplateId = "asr-exploit-guard",
                Category = "Système & sécurité",
                DefaultParameters = Defaults(("ruleIds", DefaultAsrRules)),
                Entries = new[]
                {
                    Reg(@"HKLM\SOFTWARE\Policies\Microsoft\Windows Defender\Windows Defender Exploit Guard\ASR",
                        "ExploitGuard_ASR_Rules", "DWord", "1"),
                    // Les règles ASR sont des valeurs REG_SZ (« 1 » = blocage, « 2 » = audit).
                    new TemplateRegistryEntry
                    {
                        Key = @"HKLM\SOFTWARE\Policies\Microsoft\Windows Defender\Windows Defender Exploit Guard\ASR\Rules",
                        ValueName = "${item}",
                        ValueType = "String",
                        Data = "1",
                        ForEachParameter = "ruleIds",
                    },
                },
            },

            ["audit-policy-subcategory"] = new TemplateRegistryBinding
            {
                TemplateId = "audit-policy-subcategory",
                Category = "Système & sécurité",
                DefaultParameters = Defaults(("enableSubcategoryOverride", "true")),
                Entries = new[]
                {
                    Reg(@"HKLM\SYSTEM\CurrentControlSet\Control\Lsa",
                        "SCENoApplyLegacyAuditPolicy", "DWord", "1",
                        whenParameter: "enableSubcategoryOverride", whenTrue: true),
                },
            },
        };

    private const string DefaultAsrRules =
        "BE9BA2D9-53EA-4CDC-84E5-9B1EEEE46550\n"
        + "D4F940AB-401B-4EFC-AADC-AD5F3C50688A\n"
        + "5BEB7EFE-FD9A-4556-801D-275E5FFC04CC\n"
        + "D3E037E1-3EB8-44C8-A917-57927947596D\n"
        + "7674BA52-37EB-4664-B9DA-912CEEA1E185\n"
        + "D1E49AAC-8F56-4280-B9BA-993A6D77406C\n"
        + "3B576869-3D94-466F-856C-7AE89D66FBF9\n"
        + "9E6C4E1F-7D60-472F-BA1A-A39EF669E4B2";

    private static TemplateRegistryEntry UrlList(string key, string valueName, string parameter, string browser)
        => new()
        {
            // Les stratégies de liste Edge/Chrome sont des sous-clés portant le
            // nom de la stratégie (URLBlocklist, URLAllowlist), avec des valeurs
            // indexées « 1 », « 2 »… Il faut donc les séparer explicitement.
            Key = key + "\\" + valueName,
            ValueName = "${index}",
            ValueType = "String",
            Data = "${item}",
            WhenParameter = "browser",
            WhenEquals = browser,
            ForEachParameter = parameter,
        };

    private static TemplateRegistryEntry Reg(
        string key,
        string valueName,
        string valueType,
        string data,
        string? whenParameter = null,
        string? whenEquals = null,
        string? whenNotEquals = null,
        bool? whenTrue = null)
        => new()
        {
            Key = key,
            ValueName = valueName,
            ValueType = valueType,
            Data = data,
            WhenParameter = whenParameter,
            WhenEquals = whenEquals,
            WhenNotEquals = whenNotEquals,
            WhenTrue = whenTrue,
        };

    private static IReadOnlyDictionary<string, string> Defaults(params (string Name, string Value)[] values)
    {
        var dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in values)
        {
            dictionary[name] = value;
        }

        return dictionary;
    }
}
