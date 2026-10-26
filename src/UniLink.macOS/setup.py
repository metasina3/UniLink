from setuptools import setup, find_packages

setup(
    name="unilink-macos",
    version="1.3.0",
    description="UniLink for macOS — University Wi-Fi Captive Portal Client",
    packages=find_packages(),
    include_package_data=True,
    package_data={"unilink": ["gui/*"]},
    install_requires=[
        "rumps>=0.4.0",
        "pyobjc-core>=11.0",
        "pyobjc-framework-Cocoa>=11.0",
        "pyobjc-framework-SystemConfiguration>=11.0",
        "pyobjc-framework-WebKit>=11.0",
    ],
    entry_points={
        "console_scripts": [
            "unilink = unilink.cli:main",
            "unilink-menubar = unilink.menubar:main",
        ],
    },
)
