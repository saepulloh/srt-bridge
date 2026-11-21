using GDI.Core.Models;
using GDI.Hardware.Validation;
using Xunit;

namespace GDI.Tests;

public class HardwareValidationTests
{
    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public HardwareValidationTests(Xunit.Abstractions.ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Validator_DetectsInputPortDirectionConflict()
    {
        var config = new IoRoutingConfig
        {
            Devices = [
                new IoDevice
                {
                    Id = "decklink_1",
                    Model = "DeckLink Mini Monitor 4K",
                    Ports = [
                        new IoPort
                        {
                            Id = "sdi_out_1",
                            Name = "SDI Out",
                            Direction = "Output",
                            CanInput = false,
                            CanOutput = true
                        }
                    ]
                }
            ],
            Inputs = [
                new InputAssignment
                {
                    SourceId = Guid.NewGuid().ToString(),
                    DeviceId = "decklink_1",
                    PortId = "sdi_out_1"
                }
            ]
        };

        var result = PortConflictValidator.Validate(config, []);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("does not support video input"));
    }

    [Fact]
    public void Validator_DetectsDuplicatePhysicalInputPortConflict()
    {
        var ch1 = Guid.NewGuid();
        var ch2 = Guid.NewGuid();

        var config = new IoRoutingConfig
        {
            Devices = [
                new IoDevice
                {
                    Id = "decklink_quad",
                    Model = "DeckLink Quad 2",
                    Ports = [
                        new IoPort
                        {
                            Id = "sdi_in_1",
                            Name = "SDI 1",
                            Direction = "Input",
                            CanInput = true,
                            CanOutput = false
                        }
                    ]
                }
            ],
            Inputs = [
                new InputAssignment { SourceId = ch1.ToString(), DeviceId = "decklink_quad", PortId = "sdi_in_1" },
                new InputAssignment { SourceId = ch2.ToString(), DeviceId = "decklink_quad", PortId = "sdi_in_1" }
            ]
        };

        var result = PortConflictValidator.Validate(config, []);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Physical input port conflict"));
    }

    [Fact]
    public void Validator_DetectsDuplicatePhysicalOutputPortConflict()
    {
        var config = new IoRoutingConfig
        {
            Devices = [
                new IoDevice
                {
                    Id = "decklink_duo",
                    Model = "DeckLink Duo 2",
                    Ports = [
                        new IoPort
                        {
                            Id = "sdi_out_1",
                            Name = "SDI Out 1",
                            Direction = "Output",
                            CanInput = false,
                            CanOutput = true
                        }
                    ]
                }
            ],
            Outputs = [
                new OutputAssignment { Output = "program", DeviceId = "decklink_duo", PortId = "sdi_out_1" },
                new OutputAssignment { Output = "multiview", DeviceId = "decklink_duo", PortId = "sdi_out_1" }
            ]
        };

        var result = PortConflictValidator.Validate(config, []);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Physical output port conflict"));
    }

    [Fact]
    public void Validator_PassesValidRouting()
    {
        var config = new IoRoutingConfig
        {
            Devices = [
                new IoDevice
                {
                    Id = "decklink_quad",
                    Model = "DeckLink Quad 2",
                    Ports = [
                        new IoPort { Id = "sdi_1", Name = "SDI 1", CanInput = true, CanOutput = false },
                        new IoPort { Id = "sdi_2", Name = "SDI 2", CanInput = true, CanOutput = false },
                        new IoPort { Id = "sdi_3", Name = "SDI 3", CanInput = false, CanOutput = true },
                        new IoPort { Id = "sdi_4", Name = "SDI 4", CanInput = false, CanOutput = true }
                    ]
                }
            ],
            Inputs = [
                new InputAssignment { SourceId = Guid.NewGuid().ToString(), DeviceId = "decklink_quad", PortId = "sdi_1" },
                new InputAssignment { SourceId = Guid.NewGuid().ToString(), DeviceId = "decklink_quad", PortId = "sdi_2" }
            ],
            Outputs = [
                new OutputAssignment { Output = "program", DeviceId = "decklink_quad", PortId = "sdi_3" },
                new OutputAssignment { Output = "multiview", DeviceId = "decklink_quad", PortId = "sdi_4" }
            ]
        };

        var result = PortConflictValidator.Validate(config, []);
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task HardwareRegistry_GetVideoInputChoices_PrintsDevices()
    {
        var reg = new GDI.Hardware.HardwareRegistry("tools/ffmpeg/bin/ffmpeg.exe");
        var choices = await reg.GetVideoInputChoicesAsync();
        Assert.NotNull(choices);
        _output.WriteLine($"Total Choices: {choices.Count}");
        foreach (var c in choices)
        {
            _output.WriteLine($"ID: {c.Id} | DisplayName: {c.DisplayName} | DeviceName: {c.DeviceName} | Backend: {c.Backend} | Vendor: {c.Vendor}");
        }
    }
}
