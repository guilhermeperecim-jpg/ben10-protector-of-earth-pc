using System.Globalization;
using Avalonia.Markup.Xaml;

namespace Ben10Launcher.Services;

/// <summary>The launcher's words in the player's language: English is the key, Portuguese comes from the table below.
/// The language: launcher.toml's <c>language</c> ("auto", "pt", "en"); "auto" follows Windows (Portuguese for any
/// pt-* language, English otherwise). Code: <c>L.T("Play")</c>, <c>L.F("{0} min so far", n)</c>; XAML:
/// <c>{l:T Play}</c>. A changed language takes effect when the window is made again (MainWindow.Reload).</summary>
public static class L
{
    public static string Setting { get; private set; } = "auto";
    public static bool Portuguese { get; private set; }

    public static void Use(string setting)
    {
        Setting = setting is "pt" or "en" ? setting : "auto";
        Portuguese = Setting == "pt" ||
                     (Setting == "auto" && CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "pt");
    }

    public static string T(string english) =>
        Portuguese && Pt.TryGetValue(english, out var pt) ? pt : english;

    public static string F(string english, params object[] args) =>
        string.Format(CultureInfo.CurrentCulture, T(english), args);

    // --- launcher.toml: the launcher's own settings (the game never reads them) ---
    public static string FilePath(GameFolder folder) => Path.Combine(folder.Data, "launcher.toml");

    public static void Load(GameFolder folder)
    {
        var setting = "auto";
        try
        {
            foreach (var line in File.Exists(FilePath(folder)) ? File.ReadAllLines(FilePath(folder)) : Array.Empty<string>())
            {
                var parts = line.Split('=', 2);
                if (parts.Length == 2 && parts[0].Trim() == "language")
                    setting = parts[1].Split('#')[0].Trim().Trim('"');
            }
        }
        catch (IOException)
        {
        }
        Use(setting);
    }

    public static void Save(GameFolder folder, string setting)
    {
        Use(setting);
        try
        {
            File.WriteAllText(FilePath(folder),
                "# The launcher's own settings (the game's are in settings.toml).\n" +
                $"language = \"{Setting}\"   # \"auto\" (as Windows), \"pt\" or \"en\"\n");
        }
        catch (IOException)
        {
        }
    }

    private static readonly Dictionary<string, string> Pt = new()
    {
        // the home page
        ["PLAY"] = "JOGAR",
        ["OPTIONS"] = "OPÇÕES",
        ["CONTROLS"] = "CONTROLES",
        ["GAME FILES"] = "ARQUIVOS DO JOGO",
        ["EXIT"] = "SAIR",
        ["Ben 10: Protector of Earth, recompiled for PC"] = "Ben 10: Protector of Earth, recompilado para PC",
        ["Ben 10: Protector of Earth - ready to play"] = "Ben 10: Protector of Earth - pronto para jogar",
        ["The game isn't built yet"] = "O jogo ainda não foi montado",
        ["The game is running"] = "O jogo está aberto",
        ["Starting the game…"] = "Abrindo o jogo…",
        ["The game is already running."] = "O jogo já está aberto.",
        ["The game closed while starting: see logs\\game.log.err"] = "O jogo fechou ao abrir: veja logs\\game.log.err",
        ["Update available: {0}"] = "Atualização disponível: {0}",
        ["Update"] = "Atualizar",
        ["Back"] = "Voltar",
        ["{0} ({1} GB), {2} CPU threads, {3} GB RAM"] = "{0} ({1} GB), {2} threads de CPU, {3} GB de RAM",
        ["{0}, {1} CPU threads, {2} GB RAM"] = "{0}, {1} threads de CPU, {2} GB de RAM",
        ["Not affiliated with Cartoon Network or D3Publisher. You need your own copy of the game."] =
            "Sem ligação com a Cartoon Network ou a D3Publisher. É preciso ter a sua própria cópia do jogo.",
        // options
        ["Options"] = "Opções",
        ["Display"] = "Tela",
        ["Graphics"] = "Gráficos",
        ["Sound"] = "Som",
        ["Launcher"] = "Launcher",
        ["Window mode"] = "Modo da janela",
        ["Borderless fullscreen"] = "Tela cheia (sem bordas)",
        ["Window"] = "Janela",
        ["Screen"] = "Monitor",
        ["Start maximised"] = "Abrir maximizada",
        ["Only in a window"] = "Só no modo janela",
        ["VSync (no tearing)"] = "VSync (sem cortes na imagem)",
        ["Picture shape"] = "Formato da imagem",
        ["Widescreen 16:9 (levels; menus and movies stay 4:3)"] = "Widescreen 16:9 (fases; menus e vídeos ficam 4:3)",
        ["4:3, as on the PS2"] = "4:3, como no PS2",
        ["Stretch to the window"] = "Esticar na janela",
        ["Drawn by"] = "Desenhado por",
        ["Graphics card (recommended)"] = "Placa de vídeo (recomendado)",
        ["Processor (slower, the PS2's own resolution)"] = "Processador (mais lento, na resolução do PS2)",
        ["Sharpness"] = "Nitidez",
        ["{0}x the PS2's detail"] = "{0}x o detalhe do PS2",
        ["16x needs a strong graphics card"] = "16x precisa de uma placa de vídeo forte",
        ["Scaling to the window"] = "Ampliação para a janela",
        ["FSR (sharp)"] = "FSR (nítido)",
        ["Smooth"] = "Suave",
        ["Blocky (pixels)"] = "Pixelado",
        ["FSR sharpening"] = "Realce do FSR",
        ["HUD, text and menus as sharp as the 3-D"] = "HUD, textos e menus tão nítidos quanto o 3D",
        ["Volume"] = "Volume",
        ["Echo (the PS2 sound chip's reverb)"] = "Eco (o reverb do chip de som do PS2)",
        ["Language"] = "Idioma",
        ["Automatic (as Windows)"] = "Automático (como o Windows)",
        ["Check for updates when the launcher opens"] = "Procurar atualizações ao abrir o launcher",
        ["Desktop shortcuts"] = "Atalhos na área de trabalho",
        ["Make them"] = "Criar",
        ["Shortcuts made on your desktop."] = "Atalhos criados na área de trabalho.",
        ["Restore defaults"] = "Restaurar padrões",
        ["(the settings of a new install)"] = "(as configurações de uma instalação nova)",
        ["Saved as you change them. They apply the next time the game opens."] =
            "Salvas ao mudar. Valem na próxima vez que o jogo abrir.",
        ["Language changes show when you go back."] = "O idioma muda quando você voltar.",
        // controls
        ["Controls"] = "Controles",
        ["Keyboard"] = "Teclado",
        ["Play with the keyboard"] = "Jogar com o teclado",
        ["Controller vibration"] = "Vibração do controle",
        ["Xbox-style controllers (XInput) work as they are. PlayStation controllers work through Steam or DS4Windows."] =
            "Controles no estilo Xbox (XInput) funcionam direto. Controles de PlayStation funcionam pelo Steam ou DS4Windows.",
        ["Controllers found now: {0}"] = "Controles encontrados agora: {0}",
        ["PS2"] = "PS2",
        ["Left stick"] = "Analógico esquerdo",
        ["D-pad"] = "Direcional",
        ["Arrow keys"] = "Setas",
        ["Right Shift"] = "Shift direito",
        ["Left Shift"] = "Shift esquerdo",
        ["Right stick"] = "Analógico direito",
        // game files
        ["Game files"] = "Arquivos do jogo",
        ["Your disc"] = "Seu disco",
        ["Checked: Ben 10: Protector of Earth, USA, version 1.01 (SLUS-21661)"] =
            "Verificado: Ben 10: Protector of Earth, EUA, versão 1.01 (SLUS-21661)",
        ["Not read yet"] = "Ainda não lido",
        ["The game"] = "O jogo",
        ["Built: {0}"] = "Montado: {0}",
        ["Not built yet"] = "Ainda não montado",
        ["Open the game's folder"] = "Abrir a pasta do jogo",
        ["Open the saves"] = "Abrir os saves",
        ["Open the logs"] = "Abrir os logs",
        ["Rebuild the game"] = "Montar o jogo de novo",
        ["Choose another disc"] = "Escolher outro disco",
        ["Rebuilding takes a while (like the first time). Your saves and settings are kept."] =
            "Montar de novo demora (como na primeira vez). Seus saves e configurações ficam.",
        ["Your saves are memory card files in game\\mc0: copy that folder to back them up."] =
            "Seus saves são arquivos de memory card em game\\mc0: copie essa pasta para ter um backup.",
        // the setup
        ["First launch"] = "Primeira vez",
        ["Choose your disc"] = "Escolha o seu disco",
        ["Your own copy of Ben 10: Protector of Earth for PlayStation 2, USA version 1.01 (SLUS-21661), as an .iso file."] =
            "A sua própria cópia de Ben 10: Protector of Earth para PlayStation 2, versão americana 1.01 (SLUS-21661), em arquivo .iso.",
        ["Next: build the game"] = "Próximo: montar o jogo",
        ["YOUR DISC"] = "SEU DISCO",
        ["Search this PC"] = "Procurar no PC",
        ["Looking for your disc on this PC…"] = "Procurando o seu disco no PC…",
        ["Found it on your PC."] = "Encontrado no seu PC.",
        ["Couldn't find it on this PC. Choose the file with Browse…, or drop it here."] =
            "Não encontrei no PC. Escolha o arquivo em Procurar… ou arraste-o aqui.",
        ["Drop the disc file here"] = "Solte o arquivo do disco aqui",
        ["Browse…"] = "Procurar…",
        ["Checking the disc…"] = "Conferindo o disco…",
        ["✓ Ben 10: Protector of Earth found"] = "✓ Ben 10: Protector of Earth encontrado",
        ["Console"] = "Console",
        ["Region"] = "Região",
        ["Version"] = "Versão",
        ["File"] = "Arquivo",
        ["That's not the disc this needs"] = "Esse não é o disco necessário",
        ["✓ Your disc was already read in an earlier try: Next carries on from there (or choose the disc again)."] =
            "✓ Seu disco já foi lido numa tentativa anterior: Próximo continua dali (ou escolha o disco de novo).",
        ["WHERE IT GOES"] = "ONDE FICA",
        ["Folder"] = "Pasta",
        ["Space"] = "Espaço",
        ["Want it somewhere else? Close this, move the folder, open it again."] =
            "Quer em outro lugar? Feche isto, mova a pasta e abra de novo.",
        ["This folder is too deep inside other folders"] = "Esta pasta está dentro de pastas demais",
        ["The build tools can't use paths this long. Close this, move the folder somewhere shorter, such as your Desktop or C:\\Games, and open it again."] =
            "As ferramentas de build não aceitam caminhos tão longos. Feche isto, mova a pasta para um lugar mais curto, como a Área de Trabalho ou C:\\Games, e abra de novo.",
        ["WHEN IT'S BUILT"] = "QUANDO ESTIVER PRONTO",
        ["Shortcuts on your desktop"] = "Atalhos na área de trabalho",
        ["\"Ben 10 Recomp\" starts the game straight away, \"Ben 10 Recomp Launcher\" opens this program. This folder always gets the game's shortcut."] =
            "\"Ben 10 Recomp\" abre o jogo direto, \"Ben 10 Recomp Launcher\" abre este programa. Esta pasta sempre ganha o atalho do jogo.",
        ["No game files come with this download. The game is built here, from your own copy, and never leaves your PC."] =
            "Nenhum arquivo do jogo vem neste download. O jogo é montado aqui, a partir da sua cópia, e nunca sai do seu PC.",
        ["About 15 GB needed"] = "Precisa de uns 15 GB",
        ["About 15 GB needed · {0} GB free"] = "Precisa de uns 15 GB · {0} GB livres",
        ["Your Ben 10: Protector of Earth disc (USA, SLUS-21661)"] = "Seu disco de Ben 10: Protector of Earth (EUA, SLUS-21661)",
        ["PS2 disc image (.iso)"] = "Imagem de disco de PS2 (.iso)",
        ["Cancel"] = "Cancelar",
        ["Open the log"] = "Abrir o log",
        ["Try again"] = "Tentar de novo",
        ["Continue"] = "Continuar",
        ["STEPS"] = "ETAPAS",
        ["What went wrong"] = "O que deu errado",
        ["Everything it printed: {0}"] = "Tudo o que ele escreveu: {0}",
        ["The game is built"] = "O jogo está montado",
        ["The setup stopped"] = "A instalação parou",
        ["Building the game from your disc"] = "Montando o jogo a partir do seu disco",
        ["Reading your disc"] = "Lendo o seu disco",
        ["Getting the build tools"] = "Preparando as ferramentas de build",
        ["Building the recompiler"] = "Montando o recompilador",
        ["Turning the game's code into C++"] = "Traduzindo o código do jogo para C++",
        ["Building the game"] = "Montando o jogo",
        ["Preparing graphics for your PC"] = "Preparando os gráficos para o seu PC",
        ["Finishing"] = "Terminando",
        ["about 1 min"] = "cerca de 1 min",
        ["about 2 min"] = "cerca de 2 min",
        ["only if they're missing"] = "só se faltarem",
        ["about 3 min"] = "cerca de 3 min",
        ["under a minute"] = "menos de 1 min",
        ["about 30-60 min"] = "cerca de 30-60 min",
        ["Starting…"] = "Começando…",
        ["Done in {0} minutes"] = "Pronto em {0} minutos",
        ["About {0} minutes left"] = "Faltam uns {0} minutos",
        ["About 1 minute left"] = "Falta cerca de 1 minuto",
        ["Under a minute so far"] = "Menos de um minuto até agora",
        ["{0} min so far"] = "{0} min até agora",
        ["The build stopped. Trying again, more slowly"] = "O build parou. Tentando de novo, mais devagar",
        ["Getting the build ready"] = "Preparando o build",
        ["Building: part {0} of {1}"] = "Montando: parte {0} de {1}",
        ["Checking which files changed: {0} files"] = "Conferindo o que mudou: {0} arquivos",
        ["Your disc was already read"] = "Seu disco já foi lido",
        ["Reading your disc: {0}%"] = "Lendo o seu disco: {0}%",
        ["Couldn't start the setup: {0}"] = "Não foi possível começar a instalação: {0}",
        ["Couldn't start the setup."] = "Não foi possível começar a instalação.",
        ["The setup stopped (exit code {0})."] = "A instalação parou (código {0}).",
        ["What the build said:"] = "O que o build disse:",
        ["This file couldn't be read as a disc."] = "Este arquivo não pôde ser lido como um disco.",
        ["Couldn't check the disc: {0}"] = "Não foi possível conferir o disco: {0}",
        ["The game isn't built yet."] = "O jogo ainda não foi montado.",
        ["The game's files from your disc aren't here ({0})."] = "Os arquivos do jogo do seu disco não estão aqui ({0}).",
        ["Couldn't make the game's shortcut in this folder: {0}"] = "Não foi possível criar o atalho do jogo nesta pasta: {0}",
        ["Couldn't make the desktop shortcuts: {0}"] = "Não foi possível criar os atalhos na área de trabalho: {0}",
        ["Building…"] = "Montando…",
        ["First launch: set up from your disc"] = "Primeira vez: instale a partir do seu disco",
        ["Ready to play"] = "Pronto para jogar",
        // updates
        ["Downloading the new version"] = "Baixando a nova versão",
        ["Checking the download"] = "Conferindo o download",
        ["Putting the new files in place"] = "Colocando os arquivos novos no lugar",
        ["the old ones kept until it works"] = "os antigos ficam até funcionar",
        ["Rebuilding the game"] = "Montando o jogo de novo",
        ["a few minutes"] = "alguns minutos",
        ["The game needs rebuilding"] = "O jogo precisa ser montado de novo",
        ["A download, then the game is rebuilt on your PC. Your saves and settings stay as they are."] = "Um download, depois o jogo é montado de novo no seu PC. Seus saves e configurações continuam como estão.",
        ["See what's new."] = "Veja as novidades.",
        ["Updating…"] = "Atualizando…",
        ["The update didn't finish"] = "A atualização não terminou",
        ["Checking…"] = "Procurando…",
        ["Check for updates"] = "Procurar atualizações",
        ["This copy is updated with git (a developer's copy)."] = "Esta cópia é atualizada pelo git (cópia de desenvolvedor).",
        ["Couldn't check: no connection to GitHub. Try again later."] = "Não deu para procurar: sem conexão com o GitHub. Tente mais tarde.",
        ["The game is open. Close it, then press Update again."] = "O jogo está aberto. Feche-o e aperte Atualizar de novo.",
        ["Not enough free space: the update needs about 1.5 GB while it runs."] = "Falta espaço livre: a atualização precisa de uns 1,5 GB enquanto roda.",
        ["Checked: the download is complete."] = "Conferido: o download está completo.",
        ["Starting the new version of this program…"] = "Abrindo a nova versão deste programa…",
        ["The update was cut off before it finished (the program closed or the PC turned off)."] = "A atualização foi interrompida antes de terminar (o programa fechou ou o PC desligou).",
        ["Rebuilding the game with the new version…"] = "Montando o jogo de novo com a nova versão…",
        ["The game is open. Close it, then open this program again to finish the update."] = "O jogo está aberto. Feche-o e abra este programa de novo para terminar a atualização.",
        ["Couldn't start the rebuild."] = "Não foi possível começar a montagem.",
        ["Your game is as it was (the older build): Play still starts it, and your saves and settings work as before."] = "Seu jogo está como estava (a montagem anterior): Jogar continua abrindo ele, e seus saves e configurações funcionam como antes.",
        ["The update can't start yet"] = "A atualização ainda não pode começar",
        ["The update couldn't finish"] = "A atualização não terminou",
        ["Check your internet connection and try again."] = "Confira sua conexão com a internet e tente de novo.",
        ["Try again: it downloads afresh."] = "Tente de novo: ele baixa do zero.",
        ["Try again when you're ready."] = "Tente de novo quando quiser.",
        ["No notes came with this version."] = "Esta versão não trouxe notas.",
        ["Try again. If it keeps failing, open the update's log and post it in an issue on GitHub."] = "Tente de novo. Se continuar falhando, abra o log da atualização e poste numa issue no GitHub.",
        ["Version {0} is available"] = "A versão {0} está disponível",
        ["What's new in {0}"] = "Novidades da {0}",
        ["UPDATE TO {0}"] = "ATUALIZAR PARA {0}",
        ["Version {0}"] = "Versão {0}",
        ["Version {0} is available."] = "A versão {0} está disponível.",
        ["Up to date (version {0}). Checked today at {1}."] = "Atualizado (versão {0}). Conferido hoje às {1}.",
        ["Updating to version {0}"] = "Atualizando para a versão {0}",
        ["Rebuilding the game for version {0}"] = "Montando o jogo de novo para a versão {0}",
        ["Couldn't start the rebuild: {0}"] = "Não foi possível começar a montagem: {0}",
        ["The rebuild stopped (exit code {0})."] = "A montagem parou (código {0}).",
        ["The game is rebuilt: version {0}"] = "O jogo foi montado de novo: versão {0}",
        ["Updated to version {0}"] = "Atualizado para a versão {0}",
        ["Up to date (version {0})."] = "Atualizado (versão {0}).",
        ["You still have version {0}, exactly as it was. Your game, saves and settings work as before."] = "Você continua com a versão {0}, exatamente como estava. Seu jogo, saves e configurações funcionam como antes.",
        ["and {0} more under What's new"] = "e mais {0} em Novidades",
        ["Update now"] = "Atualizar agora",
        ["If anything goes wrong you keep the version you have now, exactly as it is."] = "Se algo der errado, você fica com a versão que tem agora, exatamente como está.",
        ["✓ The game is rebuilt and ready. Your saves and settings are as they were."] = "✓ O jogo foi montado de novo e está pronto. Seus saves e configurações estão como antes.",
        ["All release notes on GitHub"] = "Todas as notas no GitHub",
        ["Done"] = "Pronto",
        ["WHAT'S NEW"] = "NOVIDADES",
        ["WHAT WENT WRONG"] = "O QUE DEU ERRADO",
        ["Open the update's log"] = "Abrir o log da atualização",
        ["Back to Play"] = "Voltar",
        // the disc checker's messages (tools/prepare_disc.py)
        ["not a PS2 disc image (.iso): no ISO-9660 volume at sector 16"] =
            "não é uma imagem de disco de PS2 (.iso): não há volume ISO-9660 no setor 16",
        ["SLUS_216.61 is not on this disc: the recomp is for Ben 10: Protector of Earth (USA, SLUS-21661) only"] =
            "SLUS_216.61 não está neste disco: o recomp é só para Ben 10: Protector of Earth (EUA, SLUS-21661)",
        ["this is Ben 10: Protector of Earth (USA), but not version 1.01 (or the image is damaged): the recomp needs the 1.01 disc"] =
            "este é Ben 10: Protector of Earth (EUA), mas não a versão 1.01 (ou a imagem está danificada): o recomp precisa do disco 1.01",
    };
}

/// <summary>XAML: <c>{l:T Play}</c> = <c>L.T("Play")</c>.</summary>
public sealed class TExtension : MarkupExtension
{
    public TExtension(string key) => Key = key;
    public string Key { get; set; }
    public override object ProvideValue(IServiceProvider serviceProvider) => L.T(Key);
}
