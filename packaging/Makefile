# Convenience Makefile for local (non-AUR) installs.
# Usage:
#   make build
#   sudo make install       # installs into /usr/local by default
#   sudo make uninstall

PREFIX ?= /usr/local
BINDIR ?= $(PREFIX)/bin
APPDIR ?= $(PREFIX)/share/applications
PIXDIR ?= $(PREFIX)/share/pixmaps

ROOT := $(abspath $(dir $(lastword $(MAKEFILE_LIST)))/..)
BIN  := $(ROOT)/dist/omarchy-theme-creator

.PHONY: build dev install uninstall clean

build:
	bash $(ROOT)/packaging/build.sh

# Rebuild and overwrite the currently-installed binary (fast local iteration).
dev:
	bash $(ROOT)/packaging/dev-update.sh

install:
	install -Dm755 $(BIN) $(DESTDIR)$(BINDIR)/omarchy-theme-creator
	install -Dm644 $(ROOT)/packaging/omarchy-theme-creator.desktop \
		$(DESTDIR)$(APPDIR)/omarchy-theme-creator.desktop
	@if [ -f $(ROOT)/packaging/icon.png ]; then \
		install -Dm644 $(ROOT)/packaging/icon.png \
			$(DESTDIR)$(PIXDIR)/omarchy-theme-creator.png ; \
	fi

uninstall:
	rm -f $(DESTDIR)$(BINDIR)/omarchy-theme-creator
	rm -f $(DESTDIR)$(APPDIR)/omarchy-theme-creator.desktop
	rm -f $(DESTDIR)$(PIXDIR)/omarchy-theme-creator.png

clean:
	rm -rf $(ROOT)/dist $(ROOT)/src/OmarchyThemeCreator/bin $(ROOT)/src/OmarchyThemeCreator/obj
