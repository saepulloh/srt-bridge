using GDI.Core.Models;

namespace GDI.Hardware.Validation;

public sealed record ValidationResult(bool IsValid, List<string> Errors);

public static class PortConflictValidator
{
    public static ValidationResult Validate(IoRoutingConfig config, IReadOnlyList<ChannelConfig> channels)
    {
        var errors = new List<string>();

        // 1. Device and port integrity
        foreach (var dev in config.Devices)
        {
            if (string.IsNullOrWhiteSpace(dev.Id) || string.IsNullOrWhiteSpace(dev.Model))
            {
                errors.Add($"Device entry '{dev.Id}' has missing model or identifier.");
            }

            var duplicatePortIds = dev.Ports.GroupBy(p => p.Id, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1);
            foreach (var dup in duplicatePortIds)
            {
                errors.Add($"Duplicate port ID '{dup.Key}' detected on device '{dev.Model}'.");
            }
        }

        var channelIds = channels.Select(c => c.Id.ToString()).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // 2. Validate input assignments
        var usedInputPorts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var input in config.Inputs)
        {
            if (string.IsNullOrWhiteSpace(input.DeviceId) || string.IsNullOrWhiteSpace(input.PortId))
            {
                errors.Add("Input assignment is missing device or port specification.");
                continue;
            }

            var device = config.Devices.FirstOrDefault(d => d.Id.Equals(input.DeviceId, StringComparison.OrdinalIgnoreCase));
            if (device == null)
            {
                errors.Add($"Input assigned to non-existent device ID '{input.DeviceId}'.");
                continue;
            }

            var port = device.Ports.FirstOrDefault(p => p.Id.Equals(input.PortId, StringComparison.OrdinalIgnoreCase));
            if (port == null)
            {
                errors.Add($"Port '{input.PortId}' not found on device '{device.Model}'.");
                continue;
            }

            if (!port.CanInput)
            {
                errors.Add($"Port '{port.Name}' on '{device.Model}' does not support video input.");
            }

            // Port conflict check
            var portKey = $"{input.DeviceId}/{input.PortId}";
            if (!usedInputPorts.Add(portKey))
            {
                errors.Add($"Physical input port conflict: '{port.Name}' on '{device.Model}' is assigned to multiple input sources.");
            }
        }

        // 3. Validate output assignments
        var usedOutputPorts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var output in config.Outputs)
        {
            if (output.Output is not ("program" or "multiview"))
            {
                errors.Add($"Invalid output bus target '{output.Output}'. Must be 'program' or 'multiview'.");
            }

            var device = config.Devices.FirstOrDefault(d => d.Id.Equals(output.DeviceId, StringComparison.OrdinalIgnoreCase));
            if (device == null)
            {
                errors.Add($"Output assigned to non-existent device ID '{output.DeviceId}'.");
                continue;
            }

            var port = device.Ports.FirstOrDefault(p => p.Id.Equals(output.PortId, StringComparison.OrdinalIgnoreCase));
            if (port == null)
            {
                errors.Add($"Output port '{output.PortId}' not found on device '{device.Model}'.");
                continue;
            }

            if (!port.CanOutput)
            {
                errors.Add($"Port '{port.Name}' on '{device.Model}' does not support video playback/output.");
            }

            // Output port conflict check
            var portKey = $"{output.DeviceId}/{output.PortId}";
            if (!usedOutputPorts.Add(portKey))
            {
                errors.Add($"Physical output port conflict: '{port.Name}' on '{device.Model}' is assigned to multiple output buses.");
            }
        }

        // 4. Validate channel hardware uniqueness for single-client hardware like DeckLink
        if (channels != null)
        {
            var decklinkChannels = channels
                .Where(c => c.Direction == ChannelDirection.Encoder &&
                            !string.IsNullOrWhiteSpace(c.VideoDevice) &&
                            (c.VideoDevice.Contains("Decklink", StringComparison.OrdinalIgnoreCase) ||
                             c.VideoDevice.Contains("Blackmagic", StringComparison.OrdinalIgnoreCase)))
                .GroupBy(c => c.VideoDevice, StringComparer.OrdinalIgnoreCase);

            foreach (var group in decklinkChannels)
            {
                if (group.Count() > 1)
                {
                    var channelNames = string.Join(", ", group.Select(g => $"'{g.Name}'"));
                    errors.Add($"Duplicate DeckLink hardware assignment: Device '{group.Key}' is configured on multiple channels: {channelNames}. DeckLink SDI ports can only be used by one channel at a time.");
                }
            }
        }

        return new ValidationResult(errors.Count == 0, errors);
    }
}
