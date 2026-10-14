# Teste Android no emulador

Abra o dispositivo virtual no Device Manager do Android Studio. O preset
`Android Emulator` gera um APK de depuração para x86_64.
Esse preset usa o renderizador Compatibility para evitar Vulkan por software
no emulador; o renderizador desktop continua Mobile.
O dispositivo criado para este teste se chama `Backlands_Test_API37`.

Pré-requisitos: Godot 4.7.2 .NET, templates de exportação .NET da mesma
versão, SDK Android e Java configurados em Editor Settings > Export > Android.
O projeto usa net9.0 na exportação Android e net8.0 no desktop.

Inicie o servidor em um terminal:

```powershell
Push-Location server/build/debug
.\backlands_server.exe
Pop-Location
```

Em outro terminal, na raiz do projeto:

```powershell
.\tools\android-emulator.ps1
```

O script exporta, instala o APK e encaminha TCP 7777 pelo ADB. No jogo,
use `127.0.0.1:7777` e pressione Entrar. Toques simulam cliques para andar.
O encaminhamento precisa ser refeito quando o emulador reiniciar.

O APK fica em `.playtest/android/backlands-debug.apk`. APKs, templates,
logs e credenciais de assinatura ficam fora do Git.

O plugin `addons/raw_assets` inclui PNGs e JSONs originais na exportação:
os carregadores de sprites precisam desses bytes para recortar a arte.

Para outro dispositivo ou caminhos de instalação, use os parâmetros
`-Device`, `-AndroidSdk` e `-GodotExe` do script.
Com apenas um emulador conectado, o script seleciona esse dispositivo automaticamente.
