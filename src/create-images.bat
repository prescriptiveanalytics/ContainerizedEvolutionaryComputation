ECHO OFF

ECHO clean up ressources
docker rmi cec.gecco2025.ga.img:latest
docker rmi cec.gecco2025.es.img:latest

ECHO publish and build images

Pushd .\GeneticAlgorithm
dotnet publish -c Release
xcopy ".\configurations\*" ".\bin\Release\net8.0\publish\" /Y
xcopy ".\configurations\*" ".\bin\Release\net8.0\" /Y
docker build -t cec.gecco2025.ga.img -f Dockerfile ..
Popd

Pushd .\EvolutionStrategy
dotnet publish -c Release
xcopy ".\configurations\*" ".\bin\Release\net8.0\publish\" /Y
xcopy ".\configurations\*" ".\bin\Release\net8.0\" /Y
docker build -t cec.gecco2025.es.img -f Dockerfile ..
Popd