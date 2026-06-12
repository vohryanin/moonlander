.PHONY: all help dev1 main make1 flash-dev1 flash-main

# Команда по умолчанию (если просто ввести make)
all: help

# Справка по доступным командам
help:
	@echo "Доступные команды:"
	@echo "  make dev1         - Запустить сборку из dev1 (make_from_dev1.sh)"
	@echo "  make main         - Запустить сборку из main (make_from_main.sh)"
	@echo "  make 1            - Запустить скрипт make-1.sh"
	@echo "  make flash-dev1   - Прошивка из dev1 (flash_from_dev1.sh)"
	@echo "  make flash-main   - Прошивка из main (flash_from_main.sh)"

# Таргеты для сборки
dev1:
	./make_from_dev1.sh

main:
	./make_from_main.sh

1:
	./make-1.sh

# Таргеты для прошивки
flash-dev1:
	./flash_from_dev1.sh

flash-main:
	./flash_from_main.sh
