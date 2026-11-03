# OpenAI text – klávesa Copilot

Automatické spuštění po přihlášení do Windows lze zapnout v nastavení aplikace.
Aplikace běží pod ikonou u hodin; dvojklik otevírá nastavení, pravé tlačítko nabídku. Ručně ji můžeš spustit přes `OpenAITextShortcuts.exe` nebo zástupce **OpenAI text** na ploše.

## API klíč

1. Vytvoř vlastní klíč v [OpenAI Platform](https://platform.openai.com/api-keys).
2. Vlož ho **pouze do místního okna aplikace**, do pole API klíč se skrytými znaky. Nedávej klíč do chatu ani do zdrojového kódu.
3. Stiskni **Uložit**, potom **Zavřít**. Uložení samo nevolá API.

Klíč se ukládá do `%LOCALAPPDATA%\OpenAITextShortcuts\api-key.dpapi` šifrovaný pomocí Windows DPAPI pro aktuálního uživatele. Není součástí zdrojových souborů, nastavení JSON, argumentů procesu ani logů. Aplikace jej odemyká pouze pro volání API. DPAPI chrání soubor na disku; programy běžící pod stejným účtem Windows mohou také získat přístup. Při otevření nastavení zůstává pole prázdné a uložený klíč se do něj nenačítá.

Pokud je po uložení samotný klíč ještě v aktuální schránce, aplikace aktuální schránku vyprázdní. Historii schránky Windows tím nemaže. Klíč lze změnit opětovným vložením v nastavení; odebrat ho lze po ukončení aplikace smazáním `api-key.dpapi`.

## Použití

| Zkratka | Akce |
|---|---|
| **Copilot + O** | Oprava pravopisu, gramatiky, diakritiky a interpunkce při zachování jazyka |
| **Copilot + P** | Automatický překlad čeština → angličtina nebo angličtina → čeština |
| **Copilot + L** | Vylepšení formulací, srozumitelnosti a plynulosti v původním jazyce |
| **Copilot + Z** | Vrátit původní text před poslední úspěšnou AI úpravou do schránky |

Obnovení originálu nevolá API. Uchovává se pouze poslední originál v paměti aplikace; po ukončení se zapomene. Výsledek AI úpravy spuštěné zkratkou se automaticky vloží přes Ctrl+V do původního aktivního okna, bez rozpoznávání editovatelnosti pole nebo stavu označení. V ostatních případech zůstane ve schránce a zobrazí se Hotovo — Ctrl+V. Originál přes Copilot+Z se vrací pouze do schránky. Po obnovení se u kurzoru na 1 sekundu zobrazí **Originál obnoven**.

**Stačí označit text**, podržet fyzickou klávesu s logem **Copilot**, stisknout O, P nebo L a klávesy uvolnit. Aplikace sama provede Ctrl+C v aktivním okně a zpracuje získaný text. U kurzoru se na 1 sekundu ukáže **Oprava**, **Překlad** nebo **Vylepšení**. Jakmile je výsledek ve schránce, ukáže se u aktuální polohy kurzoru na 1 sekundu **Hotovo**. Výsledek se po zkratce vloží automaticky do původního aktivního okna. Pokud uvidíš **Hotovo — Ctrl+V**, vlož výsledek ručně. Není potřeba předem ručně kopírovat.

Podporované je také **stisknout Copilot a do 3 sekund stisknout O, P nebo L** ve stejném okně. Tím fungují i klávesnice, které posílají Copilot jen jako krátký impuls. Jiná běžná klávesa čekající příkaz zruší; samotné O/P/L bez předchozího Copilotu normálně píší text.

Po stisku zkratky zůstaň ve stejném okně, dokud nezačne zpracování. Pokud kopírování selže nebo přepneš okno během kopírování, nic se neodešle. Kopírování používá standardní Ctrl+C dané aplikace; například některé editory bez výběru kopírují celý aktuální řádek, proto nejprve označ požadovaný text.

Volby **Opravit/Přeložit/Vylepšit text ze schránky** v nabídce ikony používají přímo aktuální schránku. Lze tak například přeložit označený text zkratkou a následně vylepšit vzniklý překlad přes nabídku ikony.

V nastavení můžeš stisknout samotný Copilot a ověřit zelený nápis, že byl rozpoznán. Dokud je nastavení otevřené, zkratky neposílají text. Při běhu aplikace samotný Copilot slouží jako předpona těchto zkratek a neotevírá Copilot. Po ukončení aplikace se jeho původní funkce obnoví. Běžná klávesa Windows není předpona těchto zkratek.

Podporována je obvyklá hardwarová kombinace **Win+Shift+F23**, případně samostatné F23. Konkrétní chování záleží na klávesnici, firmwaru a dalších přemapováních. Pokud se zelený nápis neobjeví, lze všechny tři akce spustit i z nabídky ikony; fyzickou klávesu je pak potřeba doladit. Aplikace běží bez administrátorských práv, zkratky nemusí fungovat v aplikacích spuštěných jako správce nebo na zabezpečené ploše Windows.

## Chování a nastavení

- Text se odesílá přímo na `https://api.openai.com/v1/responses`, pouze na vyžádání zkratkou či nabídkou. Žádné průběžné sledování schránky ani ukládání stisků kláves.
- Výchozí model je `gpt-6-luna`; lze jej změnit v nastavení. Pro Lunu se používá `reasoning.effort: none`, aby se omezilo čekání. Volání používá vlastní OpenAI API účet a jeho fakturaci.
- `store: false` vypíná uložení odpovědi pro pozdější načtení přes API; není to záruka nulové retence všech provozních dat poskytovatele.
- Limit vstupu je 16 000 znaků, čekání nejvýše 75 sekund. Současně probíhá jen jeden požadavek, chyby se automaticky neopakují.
- Automatické kopírování nahradí předchozí obsah schránky označeným textem. Při chybě API, prázdné nebo neúplné odpovědi se do schránky nezapíše chybný výsledek; zůstane v ní zkopírovaný text (nebo novější obsah, který jsi mezitím zkopíroval).
- Pokud během čekání zkopíruješ jiný obsah, aplikace ho nepřepíše. Dokončený výsledek lze získat volbou **Zkopírovat poslední výsledek** v nabídce ikony.
- Poslední výsledek zůstává pouze v paměti aplikace, dokud nevznikne nový nebo aplikaci neukončíš. Výsledky ve schránce podléhají tvému nastavení historie a synchronizace schránky Windows.
- Automatické spuštění řídí políčko **Spouštět automaticky po přihlášení do Windows** v nastavení. Používá uživatelskou úlohu `OpenAITextShortcuts-<SID uživatele>` v Plánovači úloh Windows, bez nastaveného zpoždění po přihlášení. Funguje i na baterii a nemá časový limit běhu. Nevyžaduje uložení hesla ani běh jako správce. Vypnutí políčka odstraní úlohu i starší registrační záznam.
- Původní způsob přes registr `Run` mohl Windows po přihlášení odkládat. Při přechodu na úlohu se tento starší záznam odstraní. Opakované automatické spuštění neotevírá Nastavení ani nezablokuje zkratky v již běžící aplikaci.
- Zachytávání kláves běží ve vlastním vlákně, aby je neblokovala schránka nebo okna aplikace. Po probuzení a odemknutí se obnoví; obnovuje se také pravidelně při nečinnosti. Ručně lze použít **Obnovit zachytávání kláves** v nabídce ikony.

## Instalace

Stáhni nebo naklonuj repozitář do stálé složky ve Windows, spusť build.ps1 a potom OpenAITextShortcuts.exe. V místním nastavení vlož svůj vlastní API klíč a případně zapni automatické spouštění. Repozitář neobsahuje žádné přihlašovací údaje ani osobní nastavení.

## Sestavení a ověření

Bez instalace dalších knihoven, pomocí .NET Framework ve Windows:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\test.ps1
```

Testy používají simulovaný HTTP transport a simulované kopírování výběru: nečtou skutečný klíč, nečtou schránku a nevolají síť. Ověřují Unicode, zpracování odpovědí včetně odmítnutí a neúplného výstupu, limity vstupu, oddělení režimů, chyby API, čekání na uvolnění zkratky, neúspěšné kopírování, změnu aktivního okna a krátkodobě obsazenou schránku. Skutečné API a fyzické kombinace se ověří po místním zadání klíče.

Odebrání: vypni automatické spuštění, ukonči aplikaci z nabídky ikony a smaž složku aplikace, zástupce na ploše a podle potřeby její vlastní složku v LocalAppData.

## Dokumentace

- [OpenAI: autentizace a ochrana API klíčů](https://developers.openai.com/api/reference/overview#authentication)
- [OpenAI: generování textu a struktura odpovědi](https://developers.openai.com/api/docs/guides/text)
- [OpenAI: GPT-6 Luna](https://developers.openai.com/api/docs/models/gpt-6-luna)
- [Microsoft: ochrana dat DPAPI](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.protecteddata?view=netframework-4.8.1)
- [Microsoft: rozdíly v implementaci klávesy Copilot](https://support.microsoft.com/en-us/accessibility/windows/copilot/understand-updates-to-the-copilot-key-on-windows-devices)
- [Microsoft: posílání kláves pomocí SendInput](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput)
- [Microsoft: odkládání programů spouštěných přes Run](https://learn.microsoft.com/en-us/windows/win32/setupapi/run-and-runonce-registry-keys)
- [Microsoft: úloha při přihlášení uživatele](https://learn.microsoft.com/en-us/windows/win32/taskschd/logontrigger)
