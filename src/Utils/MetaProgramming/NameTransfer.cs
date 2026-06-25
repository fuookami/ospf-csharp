#nullable enable

using System.Collections.Generic;

namespace Fuookami.Ospf.Utils.MetaProgramming
{
    /// <summary>
    /// 名称转换器 / Name transfer (mirrors ospf-kotlin NameTransfer).
    /// Converts names between frontend and backend naming systems.
    /// </summary>
    public sealed class NameTransfer
    {
        /// <summary>前端命名系统 / Frontend naming system.</summary>
        public NamingSystem Frontend { get; }

        /// <summary>后端命名系统 / Backend naming system.</summary>
        public NamingSystem Backend { get; }

        /// <summary>缩写集 / Abbreviations set.</summary>
        public IReadOnlySet<string>? Abbreviations { get; }

        /// <summary>构造函数 / Constructor.</summary>
        public NameTransfer(NamingSystem frontend, NamingSystem backend, IReadOnlySet<string>? abbreviations = null)
        {
            Frontend = frontend;
            Backend = backend;
            Abbreviations = abbreviations;
        }

        /// <summary>前端→后端转换 / Frontend→Backend conversion.</summary>
        public string Invoke(string name) =>
            Backend.Backend(Frontend.Frontend(name, Abbreviations), Abbreviations);

        /// <summary>后端→前端转换 / Backend→Frontend conversion.</summary>
        public string Reverse(string name) =>
            Frontend.Backend(Backend.Frontend(name, Abbreviations), Abbreviations);
    }
}
