using System.Collections.Generic;
using Autodesk.Navisworks.Api;

namespace Virtuart4DNavisworks
{
    internal sealed class SetCacheEntry
    {
        public string Nome { get; }
        public string NomeGravacao { get; }
        public string Tipo { get; }
        public SavedItem Item { get; }
        public ModelItemCollection Itens { get; }
        public HashSet<ModelItem> ItensSet { get; }

        public SetCacheEntry(string nome, string nomeGravacao, string tipo, SavedItem item, ModelItemCollection itens)
        {
            Nome = nome;
            NomeGravacao = nomeGravacao;
            Tipo = tipo;
            Item = item;
            Itens = itens ?? new ModelItemCollection();
            ItensSet = new HashSet<ModelItem>();

            foreach (ModelItem mi in Itens)
                ItensSet.Add(mi);
        }
    }

    internal static class SelectionSetCache
    {
        public static List<SetCacheEntry> Collect(Document doc)
        {
            var resultado = new List<SetCacheEntry>();
            if (doc?.SelectionSets?.RootItem is GroupItem group)
                Coletar(doc, group, "", resultado);
            return resultado;
        }

        private static void Coletar(Document doc, GroupItem group, string prefixo, List<SetCacheEntry> resultado)
        {
            foreach (var child in group.Children)
            {
                if (child is SelectionSet ss)
                {
                    var itens = ObterItensDoSet(doc, ss);
                    if (itens.Count == 0) continue;

                    var tipo = ss.HasExplicitModelItems ? "Selection" : (ss.HasSearch ? "Search" : "Other");
                    resultado.Add(new SetCacheEntry(
                        prefixo + ss.DisplayName,
                        ss.DisplayName,
                        tipo,
                        ss,
                        itens));
                }
                else if (child is GroupItem grp)
                {
                    Coletar(doc, grp, prefixo + grp.DisplayName + " > ", resultado);
                }
            }
        }

        private static ModelItemCollection ObterItensDoSet(Document doc, SelectionSet ss)
        {
            var coll = new ModelItemCollection();
            if (ss.HasExplicitModelItems)
                coll.AddRange(ss.ExplicitModelItems);
            else if (ss.HasSearch && ss.Search != null)
                coll.AddRange(ss.Search.FindAll(doc, false));
            return coll;
        }
    }
}
