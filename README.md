# RecoveryLab

Aplicação WPF para inspecionar, em modo somente leitura, uma região contígua de 64 clusters a partir do início da MFT de um volume NTFS local. A interface permite pesquisar e filtrar os registros encontrados e consultar seus metadados. A leitura do conteúdo e a recuperação de arquivos ficam pendentes.

## Executar

No Windows, com o SDK do .NET 10 instalado, execute na raiz do projeto.

```powershell
dotnet run --project RecoveryLab.csproj
```

Selecione um volume e clique em **Iniciar análise**. Se o Windows negar acesso ao volume, abra o RecoveryLab como administrador.

Para gerar o executável Windows x64, use `dotnet publish RecoveryLab.csproj -p:PublishProfile=FolderProfile`. O executável fica em `bin/publish/win-x64/` e requer o .NET Desktop Runtime 10 x64.

## Continuar o backend

- [Analysis/MftScan.cs](Analysis/MftScan.cs) localiza e percorre a região inicial da MFT. Comece aqui para ampliar a varredura.
- [MftRecordParser.cs](MftRecordParser.cs) interpreta registros e data runs. Os métodos de diagnóstico foram mantidos e os métodos `Read*` retornam os metadados usados pela interface.
- [RawDiskReader.cs](RawDiskReader.cs) lê o volume selecionado. [Analysis/MftAnalysisService.cs](Analysis/MftAnalysisService.cs) reúne os resultados para a interface.
