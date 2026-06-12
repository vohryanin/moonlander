# Keyboard Layout Sync Agent

Маленький Windows tray-agent для синхронизации языка ОС с внутренним языком клавиатуры.

Агент раз в 300 мс смотрит язык активного окна Windows:

- английский язык отправляет в клавиатуру как `EN`
- русский язык отправляет в клавиатуру как `RU`

В прошивке это принимается через QMK Raw HID и обновляет внутреннее состояние `lang_shift` без отправки дополнительных клавиш в ОС.

## Сборка

На Windows запустить:

```powershell
.\windows-agent\KeyboardLayoutSyncAgent\build.ps1
```

Готовый файл появится здесь:

```text
windows-agent\KeyboardLayoutSyncAgent\bin\KeyboardLayoutSyncAgent.exe
```

Для сборки используется встроенный в Windows компилятор .NET Framework, отдельный .NET SDK не нужен.

## Запуск

Запустить `KeyboardLayoutSyncAgent.exe`. В трее появится иконка `Keyboard layout sync`.

Двойной клик по иконке делает принудительную синхронизацию. В меню иконки есть `Sync now` и `Exit`.

Если в подсказке написано `Raw HID not found`, значит клавиатура еще не прошита версией с `RAW_ENABLE = yes`, либо Windows пока не увидела Raw HID endpoint.
