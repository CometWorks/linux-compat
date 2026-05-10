.PHONY: all configure build clean

all: build

configure:
	cmake --preset default

build: configure
	cmake --build --preset default

clean:
	rm -rf build
