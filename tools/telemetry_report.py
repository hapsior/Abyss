"""Raport z telemetrii (pliki JSONL gracza albo bota): obrażenia na pokój walki wg krainy, źródła trafień."""
import json, sys, collections, statistics as st
def load(path):
    return [json.loads(l) for l in open(path, encoding='utf-8') if l.strip()]
def report(name, rows):
    print(f"\n=== {name}: {len(rows)} pokoi")
    combat=[r for r in rows if r['room'] in ('Combat','Exit')]
    boss=[r for r in rows if r['room']=='Boss']
    by=collections.defaultdict(list)
    for r in combat: by[(r['depth']-1)//5].append(r)
    print(" kraina | pokoje | % maks.HP utracone / pokój (śr / mediana) | trafień/pokój | s/pokój | zabójstw/pokój")
    for b in sorted(by):
        rs=by[b]; pct=[100*r['damage']/max(1,r['max_hp']) for r in rs]
        print(f"  {b+1:5} | {len(rs):6} | {st.mean(pct):6.1f} / {st.median(pct):5.1f} | {st.mean(r['hits'] for r in rs):5.2f} | {st.mean(r['seconds'] for r in rs):5.1f} | {st.mean(r['kills'] for r in rs):5.1f}")
    if boss:
        print(" bossowie: piętro, % maks.HP utracone, trafienia, czas")
        bb=collections.defaultdict(list)
        for r in boss: bb[r['depth']].append(r)
        for d in sorted(bb):
            rs=bb[d]; print(f"  {d:3}: {st.mean(100*r['damage']/max(1,r['max_hp']) for r in rs):6.1f}%  trafień {st.mean(r['hits'] for r in rs):4.1f}  {st.mean(r['seconds'] for r in rs):5.1f} s")
    src=collections.Counter(); hits=collections.Counter()
    for r in rows:
        for k,v in r.get('damage_by',{}).items(): src[k]+=v
        for k,v in r.get('hits_by',{}).items(): hits[k]+=v
    tot=sum(src.values()) or 1
    print(" źródła obrażeń (top 10):", ', '.join(f"{k} {100*v/tot:.0f}% ({hits[k]}×)" for k,v in src.most_common(10)))
for p in sys.argv[1:]:
    report(p.split('/')[-1], load(p))
