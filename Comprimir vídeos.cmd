@echo off
rem Lanzador de la interfaz de compresion de video (doble clic)
start "" pwsh -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "%~dp0Shrink-Video-GUI.ps1"
