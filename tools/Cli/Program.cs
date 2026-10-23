using Cli;

ICommand[] commands =
[
    new YamlValidateCommand(),
    new YamlAotDirectivesCommand(),
    new ReplayInfoCommand(),
    new ReplayVerifyCommand(),
    new ReplayRerecordCommand(),
];

return CommandRunner.Run( commands, args );
