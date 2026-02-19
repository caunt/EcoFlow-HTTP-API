package main

import (
	"os"
	"path/filepath"

	"google.golang.org/protobuf/proto"
	"google.golang.org/protobuf/types/descriptorpb"

	"github.com/jhump/protoreflect/desc"
	"github.com/jhump/protoreflect/desc/protoprint"
)

func main() {
	if len(os.Args) != 3 {
		os.Stderr.WriteString("usage: decoder <file_descriptor_set.pb> <out_directory>\n")
		os.Exit(2)
	}

	descriptorSetPath := os.Args[1]
	outputDirectoryPath := os.Args[2]

	descriptorSetBytes, readError := os.ReadFile(descriptorSetPath)
	if readError != nil {
		panic(readError)
	}

	var fileDescriptorSet descriptorpb.FileDescriptorSet
	if unmarshalError := proto.Unmarshal(descriptorSetBytes, &fileDescriptorSet); unmarshalError != nil {
		panic(unmarshalError)
	}

	fileDescriptors, buildError := desc.CreateFileDescriptorsFromSet(&fileDescriptorSet)
	if buildError != nil {
		panic(buildError)
	}

	protoPrinter := protoprint.Printer{}

	for _, fileDescriptor := range fileDescriptors {
		outputFilePath := filepath.Join(outputDirectoryPath, fileDescriptor.GetName())
		if mkdirError := os.MkdirAll(filepath.Dir(outputFilePath), 0o755); mkdirError != nil {
			panic(mkdirError)
		}

		outputFile, createError := os.Create(outputFilePath)
		if createError != nil {
			panic(createError)
		}

		printError := protoPrinter.PrintProtoFile(fileDescriptor, outputFile)
		closeError := outputFile.Close()
		if printError != nil {
			panic(printError)
		}
		if closeError != nil {
			panic(closeError)
		}
	}
}
