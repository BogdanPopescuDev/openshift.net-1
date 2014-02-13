$currentDir = split-path $SCRIPT:MyInvocation.MyCommand.Path -parent
Import-Module (Join-Path $currentDir '..\common\openshift-common.psd1') -DisableNameChecking

$helpCommands = "help", "--help", "-help", "/?", "-?"

if ($helpCommands -contains $args[0])
{
    Get-Help OO-Accept-Node
}
else
{
    $argsString = [string]::Join(" ", $args)
    $output = Invoke-Expression "OO-Accept-Node $argsString"
    write-Output $output.Output
    exit $output.ExitCode
}

exit 0