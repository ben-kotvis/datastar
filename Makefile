initialize: 
	curl -fsSL https://opencode.ai/install | bash
	dotnet restore ${PWD}
	dotnet build
	dotnet dev-certs https --trust