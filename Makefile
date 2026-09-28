initialize: 
	dotnet restore ${PWD}/datastar.sln
	dotnet build ${PWD}/datastar.sln