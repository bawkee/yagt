Remove-Item -Path build\YAGT\*.pdb -Force
Compress-Archive -Path build\YAGT -DestinationPath "build\yagt_${env:VERSION}.${env:RUNTIME}.zip" -Force