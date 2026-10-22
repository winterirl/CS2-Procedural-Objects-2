using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Colossal;
using Colossal.Localization;

namespace PO2.Localization
{
    internal sealed class LocalizationService : IDisposable
    {
        private readonly LocalizationManager m_Manager;
        private readonly Setting m_Setting;
        private readonly Assembly m_Assembly = typeof(LocalizationService).Assembly;
        private readonly Dictionary<string, string> m_English;
        private readonly Dictionary<string, IDictionarySource> m_Sources = new Dictionary<string, IDictionarySource>(StringComparer.Ordinal);
        private bool m_Registering;
        private bool m_Disposed;

        internal LocalizationService(LocalizationManager manager, Setting setting)
        {
            m_Manager = manager;
            m_Setting = setting;
            m_English = LocaleCatalog.ReadEnglish(ReadResource("en-US"));
            try
            {
                RegisterSupportedLocales();
                m_Manager.onSupportedLocalesChanged += RegisterSupportedLocales;
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        private string ReadResource(string locale)
        {
            using (var stream = m_Assembly.GetManifestResourceStream("PO2.L10n." + locale + ".json"))
            {
                if (stream == null) return null;
                using (var reader = new StreamReader(stream)) return reader.ReadToEnd();
            }
        }

        private void RegisterSupportedLocales()
        {
            if (m_Disposed || m_Registering) return;
            m_Registering = true;
            try
            {
                Register("en-US");
                foreach (string locale in m_Manager.GetSupportedLocales()) Register(locale);
            }
            finally
            {
                m_Registering = false;
            }
        }

        private void Register(string locale)
        {
            if (m_Sources.ContainsKey(locale)) return;
            var entries = LocaleCatalog.Overlay(m_English, locale == "en-US" ? null : ReadResource(locale), warning => Mod.log.Warn("[PO Localization " + locale + "] " + warning));
            SettingsLocales.AddAliases(entries, m_Setting);
            var source = new MemorySource(entries);
            // AddSource can announce a locale change while we are registering it.
            m_Sources.Add(locale, source);
            m_Manager.AddSource(locale, source);
        }

        public void Dispose()
        {
            if (m_Disposed) return;
            m_Disposed = true;
            m_Manager.onSupportedLocalesChanged -= RegisterSupportedLocales;
            foreach (var source in m_Sources) m_Manager.RemoveSource(source.Key, source.Value);
            m_Sources.Clear();
        }
    }
}