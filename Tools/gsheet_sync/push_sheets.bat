@echo off
chcp 65001
cd /d "%~dp0"
python push_sheets.py
pause
