# Metal Archives — correspondencia

Registro de lo que se les ha dicho. **Lo escrito aquí compromete al proyecto**: ver invariantes 2 y 3 de `CLAUDE.md` y `D7` en `DECISIONS.md`.

## Contexto

- Contacto: `webmaster@metal-archives.com` (obfuscado en su página de Support como la cadena invertida `moc/sevihcra-latem//retsambew`; `//` → `@`, `/` → `.`). **No verificado cargando la página**: `metal-archives.com` devuelve `403 Forbidden` a peticiones automatizadas. Alternativa oficial: [su foro](https://forum.metal-archives.com).
- Webmasters fundadores: **Hellblazer** y **Morrigan**.
- Tienen página oficial de [Tools & add-ons](https://www.metal-archives.com/content/tools) donde listan proyectos de terceros construidos sobre sus datos.
- Precedente documentado de exportaciones bajo petición: ver `D8`.

---

## 1. Presentación — enviado 2026-07-10

No pide nada. Su función es abrir la puerta y dejar constancia de que no se les scrapea.

**Asunto:** Grimoire — a free discovery tool that sends people back to Metallum

> Hi,
>
> I'm Pedro, a developer from Spain and a long-time Metallum user. I'm building something small and I'd rather tell you about it now than have you find out later.
>
> It's called **Grimoire**. It's a free, non-commercial discovery tool for metal and rock — right now just for me and a handful of friends, and I have no plans to monetise it.
>
> The premise: we all keep listening to the same records. Not because recommendations are scarce, but because we filter by label before we filter by ear — you read "technical brutal death from Slovakia" and you skip. So Grimoire serves you a band **blind**. No name, no genre, no country, no cover art. Forty-five seconds of audio. You only find out who it was if you liked it. Rarity works in reverse: discovering Metallica is worth nothing, discovering a Finnish sludge band with 300 listeners is worth everything. And it draws the family tree — who played with whom, which band came out of which break-up.
>
> Two things you should know up front.
>
> **We are not scraping you.** The data comes from MusicBrainz, Discogs, Wikidata, Last.fm and the iTunes Search API. We considered crawling Metallum, decided against it, and I'd rather say that out loud than have you wonder.
>
> **We link back to you.** Every band page in Grimoire points at its Metallum entry. Grimoire is a discovery front-end; Metallum is the reference. Once someone finds a band through us, you're where they go next. You've never tried to be a recommender, and we're never going to try to be an encyclopaedia.
>
> So this isn't a request. It's a heads-up, plus two open doors:
>
> If you have opinions about how something like this should work — or shouldn't — I'd genuinely like to hear them. You know this scene's data better than anyone alive.
>
> And if it ever turns out to be useful to your users, you're welcome to link it. No strings, no money, ever.
>
> Either way: thanks for twenty-plus years of the best music database on the internet. Most of what I know about this music, I found through you.
>
> Cheers,
> Pedro Manso
> Gijón, Spain
> `pmanso@go2chain.es`

### Lo que este correo compromete

1. **No se scrapea Metal Archives.** Dicho por escrito, sin matices.
2. **Toda ficha de banda de Grimoire enlaza a su entrada de Metallum.**
3. La app es gratuita y no comercial.

---

## 1b. Respuesta — recibida 2026-07-14

Contestaron. Literal (lo pegó Pedro en el chat; el correo vive en su bandeja):

> Hi,
>
> That sounds like a cool idea. I don't have much advice to offer, but maybe some genre filters would be useful? I like the idea that it gives you something completely random by default without providing any details, but optionally, some people might want to hear something new in a specific genre, or exclude a subgenre they really don't want to hear.
>
> If it would be helpful for you to scrape some data from MA, that's fine as long as it remains for non-commercial use and that you don't hammer the site with requests.
>
> Glad you've found MA useful over the years!

### Qué concede

**Permiso explícito de scraping**, con **dos condiciones que ahora nos obligan**:

1. **Uso no comercial.** Coincide con el invariante 1 del proyecto (coste cero, gratis), pero deja de ser una elección nuestra: es la condición del permiso. Monetizar Grimoire rompería el acuerdo con MA, no solo un principio interno.
2. **Sin martillear el sitio.** No dan una cifra. Nosotros ponemos la cifra y erramos por lo lento — ver `D42`.

Deroga la promesa unilateral del correo 1 («we are not scraping you»): la retiraron ellos, no nosotros. **El compromiso 2 del correo 1 (enlazar toda ficha a Metallum) sigue en pie** y no lo toca nadie.

### Qué sugieren (opinión, no petición)

Filtros de género **opcionales**: mantener la cata a ciegas y aleatoria **por defecto** —les gusta tal cual— pero dejar (a) pedir un género concreto, (b) **excluir** un subgénero que no quieres oír. Es una idea de producto de quien mejor conoce esta escena. Pendiente de decidir por Pedro; no se ha implementado nada.

---

## 3. Respuesta a su permiso — **ENVIADO 2026-07-14**

Sustituye al borrador §2 (que se escribió cuando *no* podíamos scrapear y por tanto **pedía**; este **ofrece**). Criterio de Pedro: **la elección es suya, y lo que no queremos es darles trabajo**. Tres puertas, en orden de menos molestia para ellos, y la de scrapear la última —solo si es la que menos les cuesta.

**Asunto:** Re: Grimoire — a free discovery tool that sends people back to Metallum

> Hi,
>
> Thank you — both for the offer and for taking the time. I want to be careful with it, so let me put the ball in your court rather than just start crawling.
>
> What I'd want is what a band's own page already holds, and nothing more:
>
> ```
> band_id, name, country, year_formed, status, genre, lyrical_themes,
> current and past line-up, aggregate review score of each release
> (just the number, not the reviews themselves)
> ```
>
> Lyrical themes are the one field that genuinely exists nowhere else — MusicBrainz and Discogs cover discographies and credits, but nobody covers themes. The rest is on the same page anyway, so it costs you nothing extra. To be explicit about what I am NOT asking for: the review texts. Those are your users' writing, they live on their own pages, and fetching them would multiply my requests tenfold for something I don't even display. The score alone is enough.
>
> Whichever of these is least work for you, I'll take. In this order, because I'd rather spend my time than yours:
>
> 1. **A one-off export.** A file, whenever it's convenient — no rush, and no need to build anything for it. Cheaper for your bandwidth than ~180k requests from me.
> 2. **An API**, if you have one or would rather expose one. I'll use it and respect whatever limits you set.
> 3. **I scrape**, since you've said that's fine. If this is genuinely the least trouble for you, say so and I'll do it properly: one request per second at most, sequential, backing off on any 429, an identifiable User-Agent with my email in it, cached so I never fetch the same page twice, run once and not repeatedly. If I ever get it wrong, tell me and I'll stop that day.
>
> One technical question either way: **do your records hold MusicBrainz IDs anywhere?** If they do, it saves me weeks and makes the matching far more accurate. If not, I'll match bands on name + country + year and simply leave the ambiguous ones unmatched rather than guess. (For members I'll be more careful still — two drummers called John Smith are not the same man, and I'd rather show no line-up than a wrong one.)
>
> Then a separate question, which is a real ask and not a formality: **band photos and logos**. They would make the app, and **I will not hotlink them** — pointing an `<img src>` at your server would mean every page view of mine costs you bandwidth forever, which is exactly the "hammering" you asked me to avoid, only in slow motion. So instead I'm asking: would you be OK with me **caching them and serving them myself**, credited and linked back to the band's Metallum page? Your servers get hit once, not a million times. And if the answer is no — or if they're not really yours to give, since photographers and bands hold those rights — that's a perfectly good answer and I'll drop it.
>
> Non-commercial it stays, and nothing gets redistributed: no dumps, no public endpoint handing your data back out. Every band page in Grimoire links to its Metallum entry, credited.
>
> One thing I'd rather say now than have you find out later. It's free because I host it myself, on a machine I already pay for, so it costs me nothing extra — and that's how I want to keep it. What I can't honestly promise you is to lose money on it. If it ever grew enough that hosting started costing real money, I'd have three options: pay for it out of pocket, ask the people using it to chip in for the bill, or shut it down. I would not take the second one without coming back to you first and asking — and if you said no, I'd take the third before I'd break my word to you. Your condition is non-commercial, and it's yours to interpret, not mine to reinterpret quietly when it gets inconvenient.
>
> On the genre filters — I thought about it properly, and I'm going to say no, which I owe you an explanation for. The whole reason Grimoire exists is that I think we filter by label *before* we filter by ear: you read "technical brutal death from Slovakia" and you skip it before a note has played. A genre picker is that reflex, rebuilt. So I'd be putting the disease back in the cure. You're right that people will want it — I'd want it — which is exactly why I don't trust it.
>
> Where you *are* right, and where I'd rather solve it: a band you truly cannot stand shouldn't keep coming back. That's not a filter, that's memory — and the app already learns it when you banish something.
>
> Thanks again. Genuinely.
>
> Cheers,
> Pedro

### Si aceptan, esto se compromete además

4. Crédito a MA en la ficha y en el repo.
5. **Cero redistribución**: ni dumps, ni endpoint público que devuelva sus datos.
6. Si scrapeamos: **≤ 1 req/s, secuencial, backoff ante 429, User-Agent con contacto, cacheado, una sola pasada**, y se para el día que lo pidan. Dicho por escrito → `D42`.

---

## 4. Segunda respuesta — recibida 2026-07-15

Contestaron al correo §3 (las tres puertas + MBIDs + imágenes + no-comercial + filtros). Literal (lo pegó Pedro):

> To be honest, if you can take care of scraping the data yourself, that's waaay less effort for me. The bandwidth is kinda meaningless. We don't have an API at the moment. Maybe someday, but I can't promise anything.
>
> Sorry, we don't have MusicBrainz IDs, though.
>
> I also can't give you any kind of official permission to use the images since we don't own them. Our usage of the logos could be covered by fair use (at least that's our reasoning) and we'll remove band photos on request, but in the end they're all just images people have randomly taken from the Internet, so I can't tell you to use them or not use them.
>
> Fair enough concerning the non-commercial stipulation.
>
> About the filters, well, I understand your point. You can do what you want, and I guess you'll see how it goes. Just for my part, though, the fact remains that if I were to use this service, and it kept making me listen to shitty black metal, then I wouldn't use it for long, you know? :)

### Qué resuelve — las tres puertas, contestadas

1. **Puerta → SCRAPE.** Export y API descartados por ellos: no tienen API (quizá futuro, sin promesa), y scrapear es lo que **menos les cuesta** («waaay less effort», el ancho de banda les da igual). Se ejerce el permiso de D42 bajo sus condiciones. → **`D48`, Q9 cerrada.**
2. **Sin MBIDs — confirmado el peor caso.** El emparejado MA↔MusicBrainz será por **nombre+país+año**, ambiguos sin match. → **`D48`, `R3` confirmado.**
3. **Imágenes — no-permiso deliberado, ni prohibición.** No son suyas para autorizarlas: logos = fair use (su razonamiento), fotos = las quitan a petición, pero *«I can't tell you to use them or not use them»*. La pelota y el riesgo son nuestros. → **`D49`: Pedro decide cachear+servir con retirada a petición.**
4. **No comercial — ratificado.** *«Fair enough.»* Cierra el bucle con D42/D47/R10.

### Qué mantienen (filtros)

Siguen prefiriendo filtros de género — con gracia: *«You can do what you want… if it kept making me listen to shitty black metal, then I wouldn't use it for long»*. **No cambia D43**: el dolor real (algo odiado que vuelve) ya lo resuelve `Banish` como memoria, no como filtro. El chiste es un buen test — verificado que Banish/repulsión basta (D33).

### Qué compromete ahora (además de §3)

7. **Se ejerce el scrape** bajo las condiciones de D42 (≤1 req/s, secuencial, backoff, UA con contacto, cacheado, una pasada, se para si lo piden).
8. **Imágenes cacheadas y servidas por nosotros**, nunca hotlinkeadas (D44/D49), creditadas + enlace a Metallum, **con retirada a petición operativa desde el día uno**.

---

## 5. Agradecimiento y cierre del bucle — ENVIADO 2026-07-15

Respuesta al §4. No pide nada: agradece, reconfirma por escrito las condiciones (scrape + imágenes), reconoce su punto sobre los filtros **sin prometer un filtro** (D43 sigue en pie: `Banish` es la respuesta), y les invita a probar la app cuando esté acabada.

**Asunto:** Re: Grimoire — a free discovery tool that sends people back to Metallum

> Hi,
>
> That settles it, then — I'll scrape, carefully, exactly the way I promised: one request per second, sequential, backing off on errors, a User-Agent with my email in it, cached so I never fetch the same page twice, and one pass only. And I'll match on name, country and year since there are no MBIDs, leaving the ambiguous ones unmatched rather than guessing. No hurry on any of it.
>
> On the images — understood, and thank you for being straight about it. I'll treat them the way you do: cached and served from my end (never hotlinked at your server), credited and linked back to the band's Metallum page, and taken down on request. If a photographer or a band ever objects, that's their call and I'll honour it.
>
> And point taken on the filters — I really did hear you. You're right that nobody sticks with a thing that keeps shoving stuff they hate at them. The way I'm handling it is memory rather than a genre picker: when you banish a band, the app learns it and stops walking you back toward that corner. We'll see how it goes, like you said — and if it turns out I'm wrong, I'll know, because I'll have stopped using it too.
>
> When it's finished and actually good, I'd love for you to try it — no pressure at all, only if you're curious. It'd mean a lot to have it seen by someone who knows this scene better than anyone.
>
> Thank you, genuinely, for all of it — the permission, the honesty, and twenty-plus years of the best music database on the internet. You've been incredibly kind.
>
> Cheers,
> Pedro

### Qué compromete

Nada nuevo — reconfirma por escrito D48 (condiciones del scrape) y D49 (imágenes: cache+servir, no hotlink, retirada a petición). Reafirma D43 (sin filtro; `Banish` cubre el dolor real). Añade una invitación informal a que prueben la app cuando esté acabada (sin compromiso de fecha).

---

## 2. Petición de subconjunto — BORRADOR, SUPERSEDIDO por §3, nunca enviado

Se escribió cuando **no** podíamos scrapear, así que **pedía** un favor. Tras su permiso (§1b) el marco cambió: ahora **ofrecemos** y la elección es suya. Se conserva por el razonamiento, no para mandarlo.

Pedía exactamente lo que Hellblazer ha dicho públicamente que se puede pedir: un subconjunto concreto, no la base.

**Asunto:** Re: Grimoire — a small, specific data request

> Hi again,
>
> Following up on my last message, and with a concrete question.
>
> I found the changelog of *Metal Archives Graphs* mentioning a dataset from Hellblazer, and the forum threads where you've said that while the full database isn't available, a specific subset can sometimes be exported. So here is a specific one.
>
> For Grimoire, the only field in Metallum that genuinely exists nowhere else is **lyrical themes**. MusicBrainz and Discogs cover line-ups, discographies and credits well enough. Nobody covers themes.
>
> What I'd ask for, if you're willing:
>
> ```
> band_id, name, country, year_formed, status, genre, lyrical_themes
> ```
>
> That's it. No reviews, no line-ups, no images. Roughly 180k rows.
>
> One technical question: do your records hold MusicBrainz IDs anywhere? If not, I'll match on name + country + year and leave the ambiguous ones unmatched — but if you have MBIDs, it saves me weeks and makes the result far more accurate.
>
> In return, every band page in Grimoire links to its Metallum entry, the data is credited to you on the page and in the repo, and nothing is redistributed — no dumps, no public endpoint serving your data back out.
>
> And if any of it is useful to you: our users tag lyrical themes for bands that don't have them yet. That's exactly the obscure tail where your own coverage is thinnest. Those contributions are yours to take, whenever you want them.
>
> If the answer is no, no hard feelings — Grimoire works without it, and I'll keep linking to you either way.
>
> Cheers,
> Pedro

### Si aceptan, esto se compromete además

4. Crédito a MA en la ficha y en el repo.
5. **Cero redistribución**: ni dumps, ni endpoint público que devuelva sus datos.
6. Las etiquetas de temática que aporten los usuarios de Grimoire quedan a su disposición.

---

## Notas

- No pedir reseñas. Encarece el favor y no hace falta para v1 (`D17`).
- El correo se mandó desde `pmanso@go2chain.es`. Se avisó a Pedro de que un dominio de empresa puede leerse como comercial; decidió mandarlo igual.
