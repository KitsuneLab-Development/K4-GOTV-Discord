<a name="readme-top"></a>

![GitHub tag (with filter)](https://img.shields.io/github/v/tag/K4ryuu/CS2-GOTV-Discord?style=for-the-badge&label=Version)
![GitHub Repo stars](https://img.shields.io/github/stars/K4ryuu/CS2-GOTV-Discord?style=for-the-badge)
![GitHub issues](https://img.shields.io/github/issues/K4ryuu/CS2-GOTV-Discord?style=for-the-badge)
![GitHub](https://img.shields.io/github/license/K4ryuu/CS2-GOTV-Discord?style=for-the-badge)
![GitHub all releases](https://img.shields.io/github/downloads/K4ryuu/CS2-GOTV-Discord/total?style=for-the-badge)
![GitHub last commit (branch)](https://img.shields.io/github/last-commit/K4ryuu/CS2-GOTV-Discord/dev?style=for-the-badge)

<!-- PROJECT LOGO -->
<br />
<div align="center">
  <h1 align="center">KitsuneLab©</h1>
  <h3 align="center">CS2 GOTV Discord</h3>
  <a align="center">Automatically handles GOTV recording, able to crop demos for every round separately. Sends the recorded demo as zipped to Discord Webhook as attachment or upload to Mega and send the url. Customizable webhook, avatar, bot name, embed and more. Automatically stop recording on idle and additionaly it can be set to be used in request mode, so it records all round separately but upload only those rounds that has been requested by users with !demo.</a>

  <p align="center">
    <br />
    <a href="https://github.com/K4ryuu/CS2-GOTV-Discord/releases">Download</a>
    ·
    <a href="https://github.com/K4ryuu/CS2-GOTV-Discord/issues/new?assignees=KitsuneLab-Development&labels=bug&projects=&template=bug_report.md&title=%5BBUG%5D">Report Bug</a>
    ·
    <a href="https://github.com/K4ryuu/CS2-GOTV-Discord/issues/new?assignees=KitsuneLab-Development&labels=enhancement&projects=&template=feature_request.md&title=%5BREQ%5D">Request Feature</a>
  </p>
</div>

### Support My Work

Your support keeps my creative engine running and allows me to share knowledge with the community. Thanks for being part of my journey.

<p align="center">
<a href="https://www.buymeacoffee.com/k4ryuu">
<img src="https://img.buymeacoffee.com/button-api/?text=Support Me&emoji=☕&slug=k4ryuu&button_colour=FF5F5F&font_colour=ffffff&font_family=Inter&outline_colour=000000&coffee_colour=FFDD00" />
</a>
</p>

<!-- ABOUT THE PROJECT -->

### Placeholder values

They all should be used in the format `{placeholder}`

- `map` - Represents the name of the server map.
- `date` - Represents the current date in the format "yyyy-MM-dd".
- `time` - Represents the current time in the format "HH:mm:ss".
- `timedate` - Represents the current date and time in the format "yyyy-MM-dd HH:mm:ss".
- `length` - Represents the duration of something, likely a demo length, formatted as "mm:ss".
- `round` - Represents the total number of rounds played in a game.
- `mega_link` - Represents whether a file has been uploaded to Mega or not.
- `requester_name` - Represents the names of the requesters, separated by commas.
- `requester_steamid` - Represents the Steam IDs of the requesters, separated by commas.
- `requester_both` - Represents both the names and Steam IDs of the requesters, formatted as "name (steamid)", separated by commas.
- `requester_count` - Represents the total count of requesters.
- `player_count` - Represents the total count of players.

Additionally, there are placeholders for multiple requesters, indexed by their count:

- `requester_name[i]` - Represents the name of the ith requester.
- `requester_steamid[i]` - Represents the Steam ID of the ith requester.
- `requester_both[i]` - Represents the name and Steam ID of the ith requester.

### Dependencies

To use this server addon, you'll need the following dependencies installed:

- [**CounterStrikeSharp**](https://github.com/roflmuffin/CounterStrikeSharp/releases): CounterStrikeSharp allows you to write server plugins in C# for Counter-Strike 2/Source2/CS2

<p align="right">(<a href="#readme-top">back to top</a>)</p>

<!-- ROADMAP -->

## Roadmap

- [ ] No plans for now

<p align="right">(<a href="#readme-top">back to top</a>)</p>

<!-- LICENSE -->

## License

Distributed under the GPL-3.0 License. See `LICENSE.md` for more information.

<p align="right">(<a href="#readme-top">back to top</a>)</p>
