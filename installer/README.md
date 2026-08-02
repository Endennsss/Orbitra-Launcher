# Orbitra Launcher Setup

Фирменный установщик собирается на базе Inno Setup 6.7+ и поддерживает русский и английский языки.

## Локальная сборка

```powershell
python publish.py windows --x64-only
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\SS14.Launcher.iss
```

Результат:

```text
bin/installer/Orbitra_Launcher_Setup_x64.exe
```

Установщик поддерживает обновление поверх существующей версии, выбор папки, ярлыки, автозапуск, тихую установку и корректное удаление. Пользовательские данные лаунчера хранятся вне каталога программы и при обновлении не удаляются.

## Тихая установка

```powershell
Orbitra_Launcher_Setup_x64.exe /VERYSILENT /NORESTART
```
