# Implementationsplan för Vasen

## Mål och avgränsning

Vasen är en privat, mobilanpassad webbsajt för ekonomin kring det gemensamma landstället. Första prioritet är att spara privata kvitton, visa och sortera kvittolistor samt summera och ersätta flera utlägg samtidigt. Budget och årsöversikt byggs i nästa etapp.

Sajten körs på den befintliga hemmaservern bredvid AZenZei och nås via den befintliga Cloudflare-lösningen. Molnlagring och nya hostingabonnemang ingår inte.

Mobilgränssnittet har högsta prioritet: skärmarna byggs först för mobil, med kvittokort, stora tryckytor och korta formulär. Desktopvarianten utvecklas därefter. Se IMPLEMENTATIONSSTATUS.md för genomförd implementation och kvarvarande arbete.

## 1. Teknisk grund

Designregel: håll sidorna kompakta med små marginaler och begränsad padding. Använd kompakta tabeller för återkommande listposter och undvik luftiga kort per rad. Prioritera mobilens överblick utan att försämra läsbarhet eller tryckytor. Sekundära förklaringar och inställningar kan vara utfällbara. Se även AGENTS.md.

| Del | Lösning |
|---|---|
| Webbapplikation | C#, ASP.NET Core och Razor Pages |
| Gränssnitt | Bootstrap, CSS och mindre mängder JavaScript |
| Databas | SQLite via Entity Framework Core |
| Inloggning | ASP.NET Core Identity |
| Kvittofiler | Lokal datamapp på hemmaservern |
| Drift | IIS på Windows Server på hemmaservern, separat från AZenZei och åtkomlig via Cloudflare |
| Utveckling | En lösning med ett webbprojekt, för Visual Studio Community Insiders och VS Code |

Sidorna läser och sparar direkt genom applikationens C#-kod. Ett separat API behövs inte. Applikationen ska kunna köras lokalt och felsökas i Visual Studio eller startas med dotnet run från VS Code.

## 2. Etapp ett: kvitton och privata utlägg

### Registrering av kvitto

Kvitton gäller alltid privata utlägg, exempelvis verktyg, färg och virke. Fasta utgifter från det gemensamma kontot registreras separat i etapp två.

Användaren ska kunna fotografera eller välja filer från mobilen och fylla i:

- Kvittodatum, förvalt till dagens datum.
- Person som gjort utlägget, förvalt till den inloggade.
- Totalbelopp i kronor. Returer registreras som separata negativa kvitton och minskar utlägg och budgetutfall på returdatumet. Köp och returer kan kvittas vid avslut för samma person; nollsumma kan avslutas utan uttag och negativ summa med återbetalning.
- Beskrivning (obligatorisk).
- Kostnadsslag (valfritt), exempelvis verktyg, färg, virke eller övrigt underhåll. Utlägg utan kostnadsslag visas som Ej kategoriserat och ingår i totalsummorna.
- Noll eller flera kvittobilder eller PDF-filer. Utlägg utan bilaga tillåts och märks tydligt med Saknar kvitto.

Datum, person, belopp och beskrivning krävs. Alla utläggare ska ha eget konto; utlägg följs per person, inte per hushåll. Personen väljs vid registreringen och kvitton flyttas sedan inte mellan personer. Kvittot blir oersatt när det sparas. Registreringsdatum och vem som registrerade det sparas separat från kvittodatum och personen som gjort utlägget.

### Avläsning av kvittobilder

Efter val och eventuell beskärning av en bild kan användaren välja Läs av. OCR körs lokalt i webbläsaren med Tesseract.js och föreslår kvittodatum och totalbelopp. Värden ändras först efter användarens aktiva val; flera möjliga träffar kräver val. Saknade eller osäkra värden kan fyllas i manuellt. Första versionen gäller bildbilagor, inte PDF. Bibliotek, motor, språkdata och licenser ligger på den egna servern. Ingen extern OCR-tjänst behövs.

### Kvittolistan blir startsida

Listan visar datum, person, beskrivning, kostnadsslag, belopp och ersättningsstatus i en kompakt tabell. På mobil samlas kompletterande uppgifter under beskrivningen i samma cell. Filter och personsummering är utfällbara så att fler kvitton ryms på skärmen.

Den får:

- Sortering på datum, person och belopp, stigande eller fallande.
- Filter på person, datumintervall, kostnadsslag och ersättningsstatus: oersatta, ersatta eller alla.
- Snabbval för år och månad. Två alltid synliga checkboxar för Visa ersatta och Bara mina kvitton uppdaterar urvalet direkt och behåller övriga filter.
- Totalsumma för hela det filtrerade urvalet, även om listan delas upp på flera sidor.
- Summering per person.
- Detaljvy med kvittofiler och eventuell koppling till uttag.

Filter och sortering ska finnas kvar när användaren återvänder från ett kvitto. På mobilen ska knappen Lägg till kvitto vara tydlig och formuläret kort.

### Ersätta flera kvitton genom ett uttag

1. Användaren markerar flera oersatta kvitton för samma person.
2. Sajten visar antal och sammanlagt belopp.
3. Användaren väljer Registrera uttag och anger uttagsdatum, faktiskt utbetalat belopp och valfri kommentar.
4. Beloppet föreslås till kvittosumman, men får ändras.
5. Alla markerade kvitton kopplas till uttaget och markeras som ersatta.

Exempel: fem kvitton på totalt 2 347 kr får ersättas genom ett uttag på 2 350 kr. Både kvittosumman och uttagsbeloppet sparas och skillnaden visas. Ingen utjämning eller exakt bankavstämning krävs.

Vid ett lägre uttag kan användaren uttryckligen välja att räkna mellanskillnaden som eget bidrag. Ett kvitto på 15 000 kr kan avslutas med 10 000 kr i uttag och 5 000 kr i eget bidrag. Hela kostnaden 15 000 kr behålls i budgeten. Bidraget tillgodoräknas kvittots person på avslutsdatumet och visas separat från bankinsättningar. Årets resultat räknar bankinsättningar plus eget bidrag minus utgifterna. Ångrat avslut återför kvittona till oersatta och utesluter bidraget. Kvittorättelser ändrar inte det registrerade bidraget automatiskt.

Ett kvitto får bara ingå i ett aktivt ersättningsuttag. En felaktig koppling ska kunna ångras med historik. Ersatta kvitton får ändras direkt, exempelvis belopp och beskrivning, med ändringshistorik. Uttagets mottagare och utbetalda belopp ändras inte automatiskt. Skillnaden mellan aktuella kvittobelopp och uttaget räknas om och visas. Personen på kvittot ändras inte. Ett uttag ska visa vilka kvitton det omfattar.

## 3. Inloggning och behörighet

Första leveransen får egna konton med lösenord och långvarig inloggning med Kom ihåg mig.

- Tre behörighetsnivåer: ägare har full åtkomst, läsare får se ekonomi och kvittofiler, och kvittoregistrerare får även skapa utlägg och rätta dem de själva registrerat fram till avslut. Uttag, budget, insättningar, gemensamma utgifter, papperskorg och användaradministration får bara ändras av ägare.
- Registreraren och ansvarig utläggare sparas separat. Alla aktiva användare, inklusive registreraren, kan väljas som ansvarig när utlägget skapas. Ansvarig utläggare kan inte ändras i efterhand. Om ett avslut ångras blir registrerarens egna utlägg redigerbara igen.
- Ägare bjuder in genom att skapa ett konto med vald läs- eller registreringsbehörighet och dela det tillfälliga lösenordet. Nya konton får läsbehörighet som standard. Alla kan byta sitt eget lösenord. Behörigheter kontrolleras på nytt vid varje förfrågan samt i de tjänster som ändrar uppgifter.
- Antalet ägare är obegränsat. Budgetens insättningsbehov delas lika mellan alla aktiva ägare. Endast ägare kan ändra ägarmarkeringen under Ändra användare; ingen maxgräns för antalet ägare finns och minst en aktiv ägare ska finnas kvar. Nya konton skapas som läsare eller kvittoregistrerare. Ägarbyte bevarar tidigare ekonomiposter och loggas i historiken.
- Ingen öppen kontoregistrering. Konton skapas av en inloggad person med namn och e-post. Båda krävs och är unika. Inloggning fungerar med namn eller e-post, oberoende av stora/små bokstäver, inklusive svenska tecken och mellanslag i namn. Ingen mailserver eller utskick av e-post ingår.
- Ett tillfälligt lösenord skapas och visas enbart direkt efter skapande eller återställning. Personen måste välja eget lösenord vid nästa inloggning innan övriga sidor eller åtgärder kan användas.
- Glömt lösenord hanteras av en annan inloggad person genom ett nytt tillfälligt lösenord. Tidigare lösenord och sessioner slutar fungera. Återställningen skickar ingen e-post.
- Konton pausas och återaktiveras, inte raderas i vanlig administration. Paus stoppar inloggning och nya registreringar för personen; tidigare ekonomiposter behålls och kan rättas. Eget konto och den sista aktiva ägaren kan inte pausas.
- Ägare kan separat anonymisera pausade konton efter uttrycklig bekräftelse. Kontots namn och inloggningsuppgifter tas bort permanent, medan kontots interna kopplingar, ägarmarkering, kvitton, insättningar och historik behålls. Fritext och bilagor ändras inte automatiskt. Anonymiserade konton kan inte återaktiveras.
- Inloggningen kan sparas i 90 dagar med förnyelse vid användning.
- Åtkomst ska kunna återkallas om en mobil tappas bort.

Passkeys för Face ID, fingeravtryck eller PIN läggs till efter att grundflödet fungerar. Biometriska uppgifter stannar på användarens enhet. Kontot ska ha en återställningsväg om mobilen försvinner. HTTPS och ett stabilt domännamn används.

## 4. Etapp två: budget och årsöversikt

### Underlag

| Underlag | Registrering |
|---|---|
| Utgifter från gemensamt konto | Flera delbelopp eller klumpsummor per kostnadsslag som summeras per år, exempelvis el, lån, försäkring och vatten |
| Privata utlägg | Summeras automatiskt från kvittona |
| Insättningar | Engångsinsättningar och automatiskt återkommande månadsinsättningar per person, fristående från banken |

Fasta utgifter betalas i sina respektive system, exempelvis Kivra och Handelsbanken. De blir aldrig privata kvitton i Vasen. Underlaget behöver inte importeras eller registreras betalning för betalning.

För varje gemensam utgift anges år, kostnadsslag, faktiskt delbelopp och kommentar. Flera poster summeras till årets utfall per kostnadsslag. Budgetbelopp anges separat per år och kostnadsslag. Budgetbeloppet kan vara ett estimat. Tomt utfall betyder ännu inte angivet, medan noll betyder ett känt nollbelopp.

För privata utlägg anges en enda gemensam budgetpott per år. Alla årets aktiva kvitton räknas mot denna pott enligt kvittodatum och belopp, oavsett kostnadsslag och ersättningsstatus. Kostnadsslag på kvitton används för filter och statistik, inte för koppling till budgetposter. Fasta kostnader från gemensamt konto har fortsatt separata budgetposter per kostnadsslag.

Exempel för 2026:

| Kostnadsslag | Budget | Utfall |
|---|---:|---:|
| El | 25 000 kr | 23 000 kr från gemensamt konto |
| Lån | 40 000 kr | 40 000 kr från gemensamt konto |
| Underhåll och material | 15 000 kr | Summeras från privata kvitton |

Lån kan vid behov delas upp i ränta och amortering. Båda kan ingå i planeringen av årets betalningsbehov.

### Sammanställningar och beräkningsregler

Årsöversikten visar:

- Budget och utfall per kostnadsslag.
- Budgeterade och registrerade insättningar. Automatiska insättningar är antagna betalningar utan bankkontroll.
- Totala utgifter och årets överskott eller underskott.
- En enkel beräkning av föreslagen års- och månadsinsättning per ägare. Befintlig kassa anges som ingående kassa per år. Insättningsbehovet är max(0, årets budget minus ingående kassa), delat lika mellan alla aktiva ägare. Månadsförslaget avrundas uppåt till hela kronor. Ingen buffertmodell eller avancerad prognos ingår.
- Privata utlägg som återstår att ersätta.
- Registrerade ersättningsuttag och eventuella avrundningsskillnader.

Ett privat utlägg räknas i utfallet enligt kvittodatum, oavsett om det är ersatt. Ersättning av kvitton räknas aldrig som ytterligare en kostnad. Budgetutfallet använder kvittobeloppet.

En separat översikt över kontots in- och utbetalningar använder det faktiskt utbetalda beloppet. Om ett beräknat kontosaldo visas utgår det från ett angivet ingående saldo, registrerade insättningar, gemensamma kontoutgifter och ersättningsuttag. Det är ett uppskattat saldo utan krav på bankavstämning. Bankkontot är frikopplat från kvittohanteringen; ett exakt banksaldo är inget krav. Gemensamma utgifter registreras mot ett år och behöver inte motsvara enskilda banktransaktioner.

Engångsinsättningar och automatiska insättningar får inte dubbelräkna samma betalning. En automatiskt skapad insättning märks med sitt ursprung och ska kunna rättas.

Bankmatchning och automatisk hämtning från Kivra eller Handelsbanken ingår inte. I etapp två ingår återkommande insättningsregler med person, månadsbelopp, betalningsdag och start-/slutdatum. Insättningar registreras automatiskt på valt datum utan manuell bekräftelse eller bankkontroll. Regler kan pausas och rättas. Skapandet ska vara idempotent: varje regel och månad ger högst en automatisk insättning, även efter omstart. Förfallna tillfällen hanteras efter driftavbrott. Själva banköverföringen ordnas separat i banken.

## 5. Datamodell

| Entitet | Huvudsakligt innehåll |
|---|---|
| Medlem | Konto, namn, ägarmarkering, läs-/registreringsbehörighet och aktiv/inaktiv |
| Kvitto | Datum, person, belopp, beskrivning, kostnadsslag och eventuell uttagskoppling |
| Kvittofil | Kvitto, lagringsnamn, ursprungligt filnamn och filtyp |
| Ersättningsuttag | Person, datum, utbetalat belopp och kommentar |
| Kostnadsslag | Namn och om det gäller gemensamma utgifter eller privata utlägg |
| Budgetpost | År, kostnadsslag och budgetbelopp |
| Gemensam utgift | År, kostnadsslag, delbelopp och kommentar; flera poster per år och kostnadsslag |
| Insättning | Datum, person, belopp, kommentar och ursprung: manuell eller automatisk |
| Insättningsregel | Person, månadsbelopp, betalningsdag, start-/slutdatum och pausstatus |
| Ändringshistorik | Vem som ändrade vad och när |

Belopp lagras i hela ören. Borttagna kvitton läggs i en papperskorg och kan återställas tills papperskorgen töms permanent. Kvitton i papperskorgen ingår inte i vanliga listor eller kostnadssummor. Om ett kvitto är kopplat till ett uttag ska uttaget bevaras och skillnaden räknas om när kvittot tas bort eller återställs. Permanent radering ska inte radera själva uttaget eller historiken över utbetalat belopp. Bilagor tas bort vid permanent radering; filhanteringen måste ta hänsyn till säkerhetskopiering. Kvitto- och uttagsändringar genomförs i databastransaktioner så att dubbel ersättning förhindras även vid samtidiga försök.

## 6. Drift och lagring

Applikationens data hålls separat från programfilerna:

```text
Vasen-data/
  database/
  receipts/
  identity-keys/
```

- Databas, kvitton och inloggningens krypteringsnycklar bevaras vid uppdatering.
- Kvittofiler hämtas genom behörighetskontrollerad åtkomst och ligger inte i en publik webbmapp.
- Tillåtna filtyper och maximal filstorlek begränsas.
- Databasen och kvittofilerna säkerhetskopieras tillsammans med en SQLite-säker metod.
- Backup ingår i hemmaserverns säkerhetskopiering och ska kunna återställas även om serverdisken går sönder.
- En återställning provas innan sajten börjar användas på riktigt.

Driftmiljön är IIS på Windows Server. Vasen får en separat IIS-webbplats och applikationspool bredvid AZenZei. Applikationen publiceras från Visual Studio eller med dotnet publish. Datamappen ligger utanför publiceringsmappen och applikationspoolens identitet får nödvändiga läs- och skrivrättigheter där.

### Publiceringsstruktur enligt AZenZei

Vasen följer samma mappupplägg som den befintliga AZenZei-installationen:

```text
Vasen/
  cloudflare/
  Current/
  Release/
    2026-11-06_14-30/
    ...
  Vasen-data/
    database/
    receipts/
    identity-keys/
```

Tidsstämpeln ovan är ett exempel. Release-mappar namnges enligt yyyy-MM-dd_HH-mm.

- Release innehåller separata, tidsstämplade publiceringar.
- Current är den aktiva publiceringen och IIS-webbplatsens sökväg.
- cloudflare följer samma lokala upplägg som AZenZei för Cloudflare-relaterad konfiguration.
- Vasen-data är beständig och delas mellan publiceringarna. Databas, kvitton och inloggningsnycklar kopieras inte in i varje release.
- Serverbundna inställningar och hemligheter hålls utanför versionshanterade publiceringsfiler och bevaras vid byte av release.

Publiceringsflöde:

1. Publicera webbprojektet till en ny tidsstämplad mapp under Release.
2. Kontrollera publiceringsinnehåll och konfiguration. Ta en återställningsbar backup före eventuella databasändringar.
3. Växla Current till den nya publiceringen med ett kort kontrollerat driftstopp för appen.
4. Starta appen och kontrollera inloggning, kvittolista och åtkomst till kvittofiler.
5. Behåll tidigare releaser för möjlighet att återgå till föregående version.

Current uppdateras med mappkopiering och app_offline.htm enligt AZenZeis Publish-ToNetworkShare.ps1. Vasens motsvarande skript publicerar till Y:\ och bevarar tidsstämplade Release-mappar. En återgång till äldre programfiler måste även ta hänsyn till kompatibilitet med genomförda databasmigreringar.

Inför installation kontrolleras Windows Server-version, installerat ASP.NET Core Hosting Bundle för vald .NET-version, domännamn och Cloudflare-konfiguration samt plats och rutin för säkerhetskopiering. Återkommande insättningar får inte förutsätta att IIS håller applikationen igång hela tiden; förfallna tillfällen ska hanteras utan dubbletter när appen startar igen.

## 7. Byggordning och godkännande

| Steg | Leverans | Klart när |
|---|---|---|
| 1 | Projektgrund, databas och inloggning | Appen startar lokalt och endast medlemmar kommer in |
| 2 | Registrering och visning av kvitton | Kvitton med flera bilagor kan sparas och öppnas från mobil |
| 3 | Lista, filter, sortering och summor | Urval och totalsummor stämmer, även över flera listsidor |
| 4 | Ersättningsuttag | Flera kvitton kan ersättas samtidigt, med avvikande belopp och möjlighet att ångra |
| 5 | Installation på hemmaservern | Appuppdatering bevarar data och backup kan återställas |
| 6 | Budget, årsöversikt och insättningar | Delbelopp och privata kvitton sammanställs utan dubbelräkning, budgeten delas lika mellan alla aktiva ägare och månadsinsättningar registreras automatiskt |
| 7 | Passkeys | Mobilinloggning och kontoåterställning fungerar |

Verifiera särskilt:

- Beloppsberäkningar och totalsummor för filtrerade urval.
- Att samma kvitto inte ersätts två gånger, även vid samtidiga försök.
- Att avvikande uttagsbelopp accepteras och sparas separat från kvittosumman.
- Att ångrad ersättning återställer kvittonas status och bevarar historik.
- Att budgeten inte dubbelräknar kvitton och ersättningsuttag.
- Att kvitton från december räknas på decemberåret även om ersättning betalas i januari.
- Att utlägg utan bilaga och utan kostnadsslag kan sparas, märks tydligt och ingår i rätt summor.
- Att ändring av ett ersatt kvitto bevarar historik och uppdaterar skillnaden mot uttaget.
- Att papperskorg och återställning ger korrekta summor och bevarar uttag.
- Att återkommande insättningar skapas utan dubbletter vid omstart eller upprepad körning.
- Att bilagor kräver inloggning.
- Att uppdatering och omstart bevarar data och inloggningsnycklar.
- Att mobilflödena fungerar i Safari på iPhone och Chrome på Android.

Första releasen omfattar steg 1–5: spara, hitta, summera och ersätta privata kvitton på den befintliga hemmaservern.





