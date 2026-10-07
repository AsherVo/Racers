using Cli;

ICommand[] commands =
[
    new YamlValidateCommand(),
    new YamlAotDirectivesCommand(),
];

return CommandRunner.Run( commands, args );
