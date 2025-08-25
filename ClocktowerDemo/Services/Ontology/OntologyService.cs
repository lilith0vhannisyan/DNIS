using System;
using System.Collections.Generic;
using VDS.RDF;
using VDS.RDF.Nodes;
using VDS.RDF.Parsing;
using VDS.RDF.Query;
using VDS.RDF.Query.Datasets;

namespace ClocktowerDemo.Services.Ontology
{
    public class OntologyService : IOntologyService
    {
        private readonly IGraph _g;
        private readonly LeviathanQueryProcessor _proc;
        private readonly NamespaceMapper _ns;

        private static readonly Uri UO = new("http://www.semanticweb.org/user/ontologies/2025/7/untitled-ontology-15/");
        private static readonly Uri CT = new("http://www.semanticweb.org/user/ontologies/2025/7/Clocktower#");
        private static readonly Uri RDF_TYPE = new("http://www.w3.org/1999/02/22-rdf-syntax-ns#type");

        private readonly IUriNode _rdfType;
        private readonly IUriNode _clsPuzzle;
        private readonly IUriNode _clsItem;
        private readonly IUriNode _pHasTrigger;
        private readonly IUriNode _pIsLocatedIn;
        private readonly IUriNode _pHasUse;
        private readonly IUriNode _pFoundAt;

        public OntologyService(string ttlPath)
        {
            _g = new Graph();
            new TurtleParser().Load(_g, ttlPath);

            _rdfType = _g.CreateUriNode(RDF_TYPE);
            _clsPuzzle = _g.CreateUriNode(new Uri(UO, "Puzzle"));
            _clsItem = _g.CreateUriNode(new Uri(UO, "Item"));
            _pHasTrigger = _g.CreateUriNode(new Uri(UO, "hasTriggerCondition"));
            _pIsLocatedIn = _g.CreateUriNode(new Uri(UO, "isLocatedIn"));
            _pHasUse = _g.CreateUriNode(new Uri(UO, "hasUse"));
            _pFoundAt = _g.CreateUriNode(new Uri(UO, "foundAt"));

            var store = new InMemoryDataset(_g);
            _proc = new LeviathanQueryProcessor(store);

            _ns = new NamespaceMapper();
            _ns.AddNamespace("", new Uri("http://www.semanticweb.org/user/ontologies/2025/7/untitled-ontology-15/"));
            _ns.AddNamespace(":", new Uri("http://www.semanticweb.org/user/ontologies/2025/7/untitled-ontology-15/"));
            _ns.AddNamespace("uo15", new Uri("http://www.semanticweb.org/user/ontologies/2025/7/untitled-ontology-15#"));
            _ns.AddNamespace("Clocktower", new Uri("http://www.semanticweb.org/user/ontologies/2025/7/Clocktower#"));
        }

        private string? QueryOne(string sparql, Action<SparqlParameterizedString>? bind = null, string col = "txt")
        {
            var s = new SparqlParameterizedString { CommandText = sparql };
            s.Namespaces = _ns;
            bind?.Invoke(s);

            var parser = new SparqlQueryParser();
            var query = parser.ParseFromString(s.ToString());

            var rs = (SparqlResultSet)_proc.ProcessQuery(query);
            if (rs.Count == 0 || !rs[0].HasValue(col)) return null;

            return rs[0][col].AsValuedNode().AsString();
        }

        private static string LN(INode node)
        {
            var s = node.ToString();
            var h = s.LastIndexOf('#');
            if (h >= 0 && h + 1 < s.Length) return s[(h + 1)..];
            var sl = s.LastIndexOf('/');
            if (sl >= 0 && sl + 1 < s.Length) return s[(sl + 1)..];
            return s;
        }

        private static bool LocalNameIs(IUriNode u, string local)
        {
            var s = u.Uri.AbsoluteUri;
            return s.EndsWith("/" + local, StringComparison.Ordinal) ||
                   s.EndsWith("#" + local, StringComparison.Ordinal);
        }

        private IUriNode? FindByLocalAndType(string local, IUriNode typeClass)
        {
            foreach (var t in _g.GetTriplesWithPredicateObject(_rdfType, typeClass))
            {
                if (t.Subject is IUriNode u && LocalNameIs(u, local))
                    return u;
            }
            return null;
        }

        private static string? AsLiteral(INode? n)
            => n is ILiteralNode lit ? lit.Value : null;

        public List<string> GetClueTextsForItem(string itemLocal)
        {
            var q = @"PREFIX ns:<http://www.semanticweb.org/user/ontologies/2025/7/untitled-ontology-15#>
                        PREFIX C:<http://www.semanticweb.org/user/ontologies/2025/7/Clocktower#>
                        SELECT ?txt WHERE {
                          ?clue a C:Clue ;
                                C:isAboutItem ns:" + itemLocal + @" ;
                                C:hasDialogueText ?txt .
                        }";
            var rs = (SparqlResultSet)_g.ExecuteQuery(q);
            var list = new List<string>();
            foreach (var r in rs) if (r["txt"] is ILiteralNode lit) list.Add(lit.Value);
            return list;
        }

        public List<(string Puzzle, string Trigger, string Location)> GetPuzzlesTriggeredByItem(string itemLocal)
        {
            // We don’t have explicit :requiresItem links, but we DO have trigger strings mentioning the emblem.
            // This finds puzzles whose trigger mentions 'emblem' (case-insensitive) and returns their location.
            var q = @"PREFIX ns:<http://www.semanticweb.org/user/ontologies/2025/7/untitled-ontology-15#>
                        PREFIX C:<http://www.semanticweb.org/user/ontologies/2025/7/Clocktower#>
                        SELECT ?p ?trigger ?loc WHERE {
                          ?p a ns:Puzzle .
                          OPTIONAL { ?p ns:isLocatedIn ?loc . }
                          OPTIONAL { ?p ns:hasTriggerCondition ?trigger . }
                          FILTER (BOUND(?trigger) && CONTAINS(LCASE(STR(?trigger)), ""emblem""))
                        }";
            var rs = (SparqlResultSet)_g.ExecuteQuery(q);
            var outp = new List<(string, string, string)>();
            foreach (var r in rs)
            {
                var name = LN(r["p"]);
                var trig = r.TryGetValue("trigger", out var t) && t is ILiteralNode tl ? tl.Value : "";
                var loc = r.TryGetValue("loc", out var l) ? LN(l) : "";
                outp.Add((name, trig, loc));
            }
            return outp;
        }

        public string? GetLocationKeyProp(string locationLocal)
        {
            var q = @"PREFIX ns:<http://www.semanticweb.org/user/ontologies/2025/7/untitled-ontology-15#>
                        SELECT ?item WHERE {
                          ns:" + locationLocal + @" ns:hasKeyProp ?item .
                        }";
            var rs = (SparqlResultSet)_g.ExecuteQuery(q);
            foreach (var r in rs) return LN(r["item"]);
            return null;
        }

        public (string? Hint, string? Item) GetHintForNpc(string npcName, int trust)
        {
            var q = $@"
                PREFIX ns1:<http://www.semanticweb.org/user/ontologies/2025/7/untitled-ontology-15/>
                PREFIX Clocktower:<http://www.semanticweb.org/user/ontologies/2025/7/Clocktower#>
                SELECT ?hint_good ?hint_neutral ?hint_bad ?item_uri ?required_trust
                WHERE {{
                    ?npc a ns1:NPC ; ns1:hasName ?name .
                    FILTER (lcase(str(?name)) = lcase(""{npcName}""))
                    OPTIONAL {{ ?npc ns1:hasGoodHint ?hint_good . }}
                    OPTIONAL {{ ?npc Clocktower:hasNeutralHint ?hint_neutral . }}
                    OPTIONAL {{ ?npc ns1:hasBadHint ?hint_bad . }}
                    OPTIONAL {{ ?npc ns1:associatedWith ?item_uri . }}
                    OPTIONAL {{
                       ?tb a Clocktower:TrustBarrier ;
                           Clocktower:isAffectedByTrustOf ?npc ;
                           Clocktower:requiresTrustScoreGreaterThanOrEqual ?required_trust .
                    }}
                }}";

            var res = (SparqlResultSet)_g.ExecuteQuery(q);
            foreach (var row in res)
            {
                // required_trust
                int required = 0;
                if (row.TryGetValue("required_trust", out var rt) && rt is ILiteralNode lit && int.TryParse(lit.Value, out var r))
                    required = r;

                // pick hint based on trust
                string? hint = null;
                if (trust >= required && row.TryGetValue("hint_good", out var hg) && hg is ILiteralNode hgl) hint = hgl.Value;
                else if (row.TryGetValue("hint_neutral", out var hn) && hn is ILiteralNode hnl) hint = hnl.Value;
                else if (row.TryGetValue("hint_bad", out var hb) && hb is ILiteralNode hbl) hint = hbl.Value;

                string? item = row.TryGetValue("item_uri", out var iu) ? LN(iu) : null;
                return (hint, item);
            }
            return (null, null);
        }

        public string? GetNpcCoreAttitude(string npcName)
        {
            var q = $@"PREFIX ns1:<http://www.semanticweb.org/user/ontologies/2025/7/untitled-ontology-15/>
                        PREFIX Clocktower:<http://www.semanticweb.org/user/ontologies/2025/7/Clocktower#>
                        SELECT ?txt WHERE {{
                          ?npc a ns1:NPC ; ns1:hasName ?name ; Clocktower:hasCoreAttitude ?txt .
                          FILTER (lcase(str(?name)) = lcase(""{npcName}""))
                        }} LIMIT 1";
            var rs = (VDS.RDF.Query.SparqlResultSet)_g.ExecuteQuery(q);
            if (rs.Count == 0 || !rs[0].TryGetValue("txt", out var n) || n is not ILiteralNode lit) return null;
            return lit.Value;
        }

        public bool? GetNpcCanInitiate(string npcName)
        {
            var q = $@"PREFIX ns1:<http://www.semanticweb.org/user/ontologies/2025/7/untitled-ontology-15/>
                        PREFIX Clocktower:<http://www.semanticweb.org/user/ontologies/2025/7/Clocktower#>
                        SELECT ?b WHERE {{
                          ?npc a ns1:NPC ; ns1:hasName ?name ; Clocktower:canInitiateConversation ?b .
                          FILTER (lcase(str(?name)) = lcase(""{npcName}""))
                        }} LIMIT 1";
            var rs = (VDS.RDF.Query.SparqlResultSet)_g.ExecuteQuery(q);
            if (rs.Count == 0 || !rs[0].TryGetValue("b", out var n) || n is not ILiteralNode lit) return null;
            return bool.TryParse(lit.Value, out var v) ? v : (bool?)null;
        }

        public List<string> GetEmotionalRange(string npcName)
        {
            var q = $@"
            PREFIX ns1:<http://www.semanticweb.org/user/ontologies/2025/7/untitled-ontology-15/>
            PREFIX Clocktower:<http://www.semanticweb.org/user/ontologies/2025/7/Clocktower#>
            SELECT ?state WHERE {{
              ?npc a ns1:NPC ; ns1:hasName ?name .
              FILTER (lcase(str(?name)) = lcase(""{npcName}""))
              ?npc Clocktower:hasEmotionalRange ?state .
            }}";
            var res = (SparqlResultSet)_g.ExecuteQuery(q);
            var list = new List<string>();
            foreach (var r in res) list.Add(LN(r["state"]));
            return list;
        }

        public string? GetCurrentEmotion(string npcName)
        {
            var q = $@"
            PREFIX ns1:<http://www.semanticweb.org/user/ontologies/2025/7/untitled-ontology-15/>
            PREFIX Clocktower:<http://www.semanticweb.org/user/ontologies/2025/7/Clocktower#>
            SELECT ?state WHERE {{
              ?npc a ns1:NPC ; ns1:hasName ?name .
              FILTER (lcase(str(?name)) = lcase(""{npcName}""))
              ?npc Clocktower:hasCurrentEmotionalState ?state .
            }}";
            var res = (SparqlResultSet)_g.ExecuteQuery(q);
            foreach (var r in res) return LN(r["state"]);
            return null;
        }

        public string DeriveEmotionForTrust(string npcName, int trust)
        {
            var rng = new HashSet<string>(GetEmotionalRange(npcName));
            var current = GetCurrentEmotion(npcName);
            string[] NEG = new[] { "Angry", "Sad", "Reserved" };
            string[] NEU = new[] { "Reserved", "Calm", "Neutral", "Joyful" };
            string[] POS = new[] { "Friendly", "Joyful", "Reserved" };

            var prefs = trust < 0 ? NEG : trust > 0 ? POS : NEU;
            foreach (var p in prefs) if (rng.Contains(p)) return p.ToLowerInvariant();
            if (current != null) return current.ToLowerInvariant();
            foreach (var e in rng) return e.ToLowerInvariant();
            return "reserved";
        }

        public List<(string Subject, string Label)> SearchByLabel(string keyword, int maxHits = 3)
        {
            var hits = new List<(string, string)>();
            var q1 = $@"
            PREFIX rdfs:<http://www.w3.org/2000/01/rdf-schema#>
            SELECT ?s ?label WHERE {{
              ?s rdfs:label ?label .
              FILTER contains(lcase(str(?label)), lcase(""{keyword}""))
            }}";
            var r1 = (SparqlResultSet)_g.ExecuteQuery(q1);
            foreach (var r in r1)
            {
                hits.Add((r["s"].ToString(), r["label"].ToString()));
                if (hits.Count >= maxHits) return hits;
            }
            if (hits.Count == 0)
            {
                foreach (var t in _g.Triples)
                {
                    var ln = LN(t.Subject);
                    if (ln.ToLowerInvariant().Contains(keyword.ToLowerInvariant()))
                    {
                        hits.Add((t.Subject.ToString(), ln));
                        if (hits.Count >= maxHits) break;
                    }
                }
            }
            return hits;
        }

        public string? GetPuzzleTriggerByLocalName(string localName)
        {
            var s = FindByLocalAndType(localName, _clsPuzzle);
            if (s == null) return null;
            var t = _g.GetTriplesWithSubjectPredicate(s, _pHasTrigger).FirstOrDefault();
            return AsLiteral(t?.Object);
        }

        public string? GetPuzzleLocationByLocalName(string localName)
        {
            var s = FindByLocalAndType(localName, _clsPuzzle);
            if (s == null) return null;
            var t = _g.GetTriplesWithSubjectPredicate(s, _pIsLocatedIn).FirstOrDefault();
            return t?.Object is INode n ? LN(n) : null;
        }

        public string? GetItemUseByLocalName(string localName)
        {
            var s = FindByLocalAndType(localName, _clsItem);
            if (s == null)
            {
                // items in your TTL are individuals like :emblem (UO namespace), so try both class check and raw local
                // fall back: any subject that ends with localName
                s = _g.Triples.SubjectNodes.OfType<IUriNode>()
                     .FirstOrDefault(u => LocalNameIs(u, localName));
                if (s == null) return null;
            }
            var t = _g.GetTriplesWithSubjectPredicate(s, _pHasUse).FirstOrDefault();
            return AsLiteral(t?.Object);
        }

        public string? GetItemFoundAtLocalName(string localName)
        {
            var s = FindByLocalAndType(localName, _clsItem)
                 ?? _g.Triples.SubjectNodes.OfType<IUriNode>().FirstOrDefault(u => LocalNameIs(u, localName));
            if (s == null) return null;
            var t = _g.GetTriplesWithSubjectPredicate(s, _pFoundAt).FirstOrDefault();
            return t?.Object is INode n ? LN(n) : null;
        }

        public List<string> FindPuzzlesByTokens(IEnumerable<string> tokens, int maxHits = 3)
        {
            var outList = new List<string>();
            var toks = tokens.Select(t => t.ToLowerInvariant()).ToArray();

            foreach (var t in _g.GetTriplesWithPredicateObject(_rdfType, _clsPuzzle))
            {
                if (t.Subject is IUriNode u)
                {
                    var ln = LN(u);
                    var lnLower = ln.ToLowerInvariant();
                    if (toks.Any(k => lnLower.Contains(k)) && !outList.Contains(ln))
                    {
                        outList.Add(ln);
                        if (outList.Count >= maxHits) break;
                    }
                }
            }
            return outList;
        }

        public List<string> FindItemsByTokens(IEnumerable<string> tokens, int maxHits = 3)
        {
            var outList = new List<string>();
            var toks = tokens.Select(t => t.ToLowerInvariant()).ToArray();

            foreach (var t in _g.GetTriplesWithPredicateObject(_rdfType, _clsItem))
            {
                if (t.Subject is IUriNode u)
                {
                    var ln = LN(u);
                    var lnLower = ln.ToLowerInvariant();
                    if (toks.Any(k => lnLower.Contains(k)) && !outList.Contains(ln))
                    {
                        outList.Add(ln);
                        if (outList.Count >= maxHits) break;
                    }
                }
            }
            return outList;
        }

        public List<string> FindItemsByTokens(List<string> tokens)
        {
            var results = new List<string>();
            if (tokens == null || tokens.Count == 0) return results;

            var toks = new HashSet<string>(tokens.Select(t => t.ToLowerInvariant()));

            // tiny synonym / expansion so "gear" hits both halves
            if (toks.Contains("gear"))
            {
                toks.Add("gear_left");
                toks.Add("gear_right");
            }

            var q = @"PREFIX ns1:<http://www.semanticweb.org/user/ontologies/2025/7/untitled-ontology-15/>
                        SELECT ?s WHERE {
                          ?s a ns1:Item .
                        }";
            var rs = (SparqlResultSet)_g.ExecuteQuery(q);

            foreach (var r in rs)
            {
                var ln = LN(r["s"]);
                var lnl = ln.ToLowerInvariant();

                // hit if any token is contained in the local name (emblem, oil, key, gear)
                if (toks.Any(t => lnl.Contains(t)))
                    results.Add(ln);
                else
                {
                    // optional: also check :hasUse text for token matches
                    var useQ = $@"PREFIX ns1:<http://www.semanticweb.org/user/ontologies/2025/7/untitled-ontology-15/>
                                    SELECT ?u WHERE {{ ns1:{ln} ns1:hasUse ?u . }}";
                    try
                    {
                        var r2 = (SparqlResultSet)_g.ExecuteQuery(useQ);
                        foreach (var row in r2)
                        {
                            var txt = row["u"].AsValuedNode().AsString().ToLowerInvariant();
                            if (toks.Any(t => txt.Contains(t)))
                            {
                                results.Add(ln);
                                break;
                            }
                        }
                    }
                    catch { /* ignore if no hasUse */ }
                }
            }
            return results.Distinct().ToList();
        }

        public List<string> FindPuzzlesByTokens(List<string> tokens)
        {
            var results = new List<string>();
            if (tokens == null || tokens.Count == 0) return results;

            var toks = new HashSet<string>(tokens.Select(t => t.ToLowerInvariant()));

            var q = @"PREFIX ns1:<http://www.semanticweb.org/user/ontologies/2025/7/untitled-ontology-15/>
                        SELECT ?s ?trig WHERE {
                          ?s a ns1:Puzzle .
                          OPTIONAL { ?s ns1:hasTriggerCondition ?trig }
                        }";
            var rs = (SparqlResultSet)_g.ExecuteQuery(q);

            foreach (var r in rs)
            {
                var ln = LN(r["s"]);
                var lnl = ln.ToLowerInvariant();
                var trig = r.TryGetValue("trig", out var tnode) ? tnode.AsValuedNode().AsString() ?? "" : "";
                var trigl = trig.ToLowerInvariant();

                if (toks.Any(t => lnl.Contains(t) || trigl.Contains(t)))
                    results.Add(ln);
            }
            return results.Distinct().ToList();
        }
    }
}
